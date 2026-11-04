using System.Globalization;
using Hrms.Application.Abstractions;
using Hrms.Domain.Platform;

namespace Hrms.Cli;

/// <summary>Which tenant databases the operator selected for migration.</summary>
internal sealed record TenantSelector(int? TenantNumber, Guid? TenantId, bool All);

/// <summary>Outcome of one tenant database's migrate-tenants pass.</summary>
internal sealed record TenantMigrationResult(int PendingCount, bool DryRun, bool CatalogueSyncFailed);

/// <summary>
/// Per-tenant portion of the migrate-tenants command. Kept separate from Program.cs
/// so the safety rules (preflight, dry-run, failure isolation, catalogue sync) are
/// unit-testable without a live database.
/// </summary>
internal static class TenantMigrationRunner
{
    /// <summary>
    /// Validates that exactly one selector was given and parses its value.
    /// Migration never targets all tenants by default: --all must be explicit.
    /// </summary>
    internal static bool TryResolveSelector(
        IReadOnlyDictionary<string, string> opts,
        out TenantSelector selector,
        out string? error)
    {
        selector = new TenantSelector(null, null, false);
        error = null;

        var hasTenantNumber = opts.ContainsKey("tenant");
        var hasTenantId = opts.ContainsKey("tenant-id");
        var migrateAll = opts.ContainsKey("all");

        if ((hasTenantNumber ? 1 : 0) + (hasTenantId ? 1 : 0) + (migrateAll ? 1 : 0) != 1)
        {
            error = "Specify exactly one selector: --tenant <number>, --tenant-id <guid>, or --all.";
            return false;
        }

        var tenantNumber = 0;
        if (hasTenantNumber &&
            (!int.TryParse(opts["tenant"], NumberStyles.None, CultureInfo.InvariantCulture, out tenantNumber) ||
             tenantNumber <= 0))
        {
            error = "--tenant must be a positive integer.";
            return false;
        }

        var tenantId = Guid.Empty;
        if (hasTenantId && !Guid.TryParse(opts["tenant-id"], out tenantId))
        {
            error = "--tenant-id must be a valid GUID.";
            return false;
        }

        selector = new TenantSelector(
            hasTenantNumber ? tenantNumber : null,
            hasTenantId ? tenantId : null,
            migrateAll);
        return true;
    }

    /// <summary>
    /// Runs preflight, migration, grants and catalogue sync for one tenant database.
    /// Throws on any preflight/migration/grant failure (caller counts and reports it).
    /// <paramref name="catalogueSync"/> runs only after a successful migration and
    /// grants — never during --dry-run; its failure is reported but not thrown,
    /// because the migration already succeeded and must stay retriable.
    /// </summary>
    internal static async Task<TenantMigrationResult> RunAsync(
        ITenantDatabaseProvisioner provisioner,
        ISecretStore store,
        TenantDatabase row,
        string tenantLabel,
        string requiredExtension,
        bool dryRun,
        Func<TenantDatabase, CancellationToken, Task>? catalogueSync,
        CancellationToken ct)
    {
        // ── Preflight: every check is read-only ──────────────────────────────
        if (row.Cluster is null)
            throw new InvalidOperationException("Tenant database has no associated cluster.");

        var adminPassword = await store.GetAsync(row.Cluster.AdminSecretRef, ct);
        if (string.IsNullOrWhiteSpace(adminPassword))
            throw new InvalidOperationException("Cluster admin secret is missing or cannot be decrypted.");

        var admin = new ClusterConnection(
            row.Cluster.Host, row.Cluster.Port, row.Cluster.AdminUsername, adminPassword);

        if (!await provisioner.DatabaseExistsAsync(admin, row.DatabaseName, ct))
            throw new InvalidOperationException("Database does not exist on the configured cluster.");

        var migratorPassword = await store.GetAsync(row.MigratorSecretRef, ct);
        if (string.IsNullOrWhiteSpace(migratorPassword))
            throw new InvalidOperationException("Migrator secret is missing or cannot be decrypted.");

        var migratorConnection = provisioner.BuildConnectionString(
            row.Cluster, row.DatabaseName, row.MigratorRoleName, migratorPassword);

        if (!await provisioner.VerifyMigratorConnectionAsync(
                migratorConnection,
                row.MigratorRoleName,
                ct))
        {
            throw new InvalidOperationException(
                $"Migrator connection validation failed. Expected role: {row.MigratorRoleName}");
        }

        var actualTenantId = await provisioner.GetTenantIdentityAsync(migratorConnection, ct);
        if (actualTenantId is null)
            throw new InvalidOperationException("Tenant identity record is missing or empty.");

        if (actualTenantId.Value != row.TenantId)
            throw new InvalidOperationException(
                $"Tenant identity mismatch. Platform tenant ID is {row.TenantId}; database tenant ID is {actualTenantId.Value}.");

        if (!await provisioner.IsExtensionAvailableAsync(migratorConnection, requiredExtension, ct))
            throw new InvalidOperationException(
                $"Required PostgreSQL extension '{requiredExtension}' is not available.");

        var appliedBefore = await provisioner.GetAppliedMigrationsAsync(migratorConnection, ct);
        var pending = await provisioner.GetPendingMigrationsAsync(migratorConnection, ct);

        Console.WriteLine();
        Console.WriteLine($"Tenant: {tenantLabel}");
        Console.WriteLine($"Database: {row.DatabaseName}");
        Console.WriteLine("Database exists: YES");
        Console.WriteLine($"Migrator connection: VALID ({row.MigratorRoleName})");
        Console.WriteLine($"Tenant identity: MATCH ({actualTenantId.Value})");
        Console.WriteLine($"Required extension {requiredExtension}: AVAILABLE");
        Console.WriteLine($"Applied migrations ({appliedBefore.Count}):");
        foreach (var migration in appliedBefore)
            Console.WriteLine($"  APPLIED  {migration}");
        Console.WriteLine($"Pending migrations ({pending.Count}):");
        foreach (var migration in pending)
            Console.WriteLine($"  PENDING  {migration}");

        if (dryRun)
        {
            Console.WriteLine("DRY RUN: no migrations, grants, seeding, or platform metadata updates were performed.");
            return new TenantMigrationResult(pending.Count, DryRun: true, CatalogueSyncFailed: false);
        }

        // Migration must succeed before grants are attempted. This also supports
        // retrying grants when migration history is already current.
        await provisioner.MigrateAsync(migratorConnection, ct);
        await provisioner.ApplyGrantsAsync(migratorConnection, row.AppRoleName, ct);

        var appliedAfter = await provisioner.GetAppliedMigrationsAsync(migratorConnection, ct);
        if (pending.Count > 0)
            row.LastMigratedAt = DateTimeOffset.UtcNow;
        row.SchemaVersion = appliedAfter.LastOrDefault();

        var catalogueSyncFailed = false;
        if (catalogueSync is not null)
        {
            try
            {
                await catalogueSync(row, ct);
            }
            catch (Exception ex)
            {
                catalogueSyncFailed = true;
                Console.Error.WriteLine($"{row.DatabaseName}: catalogue sync failed - {ex.Message}");
            }
        }

        return new TenantMigrationResult(pending.Count, DryRun: false, catalogueSyncFailed);
    }

    internal static async Task SyncCatalogueAsync(
        ITenantDatabaseProvisioner provisioner,
        ISecretStore store,
        Hrms.Application.Tenancy.TenantSeedService seeder,
        Hrms.Application.Abstractions.ITenantDbContextFactory tenantDbFactory,
        TenantDatabase row,
        CancellationToken ct)
    {
        var appPassword = await store.GetAsync(row.AppSecretRef, ct);
        if (string.IsNullOrWhiteSpace(appPassword))
        {
            // A missing or undecryptable app secret is a catalogue-sync failure,
            // not a skip: throw so RunAsync records CatalogueSyncFailed and the
            // CLI exits non-zero. Migration metadata stays saved.
            throw new InvalidOperationException(
                "application secret missing or undecryptable - catalogue sync failed.");
        }

        var appConn = provisioner.BuildConnectionString(
            row.Cluster!, row.DatabaseName, row.AppRoleName, appPassword);
        var tdb = tenantDbFactory.CreateForConnection(appConn);
        await seeder.SyncPermissionsAsync(tdb, ct);
        await seeder.SyncRoleGrantsAsync(tdb, ct);
        await seeder.SeedHrDefaultsAsync(tdb, ct);
    }
}
