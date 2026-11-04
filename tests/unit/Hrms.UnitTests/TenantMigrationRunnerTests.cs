using Hrms.Application.Abstractions;
using Hrms.Application.Modules;
using Hrms.Application.Tenancy;
using Hrms.Cli;
using Hrms.Domain.Platform;
using NSubstitute;

namespace Hrms.UnitTests;

public class TenantMigrationRunnerTests
{
    private static readonly Guid TenantGuid = Guid.NewGuid();

    private static TenantDatabase NewRow() => new()
    {
        TenantId = TenantGuid,
        DatabaseName = "hrms_tenant_1008",
        AppRoleName = "hrms_app_1008",
        AppSecretRef = "secrets/app",
        MigratorRoleName = "hrms_migrator_1008",
        MigratorSecretRef = "secrets/migrator",
        Status = TenantDatabaseStatus.Online,
        Cluster = new DbCluster
        {
            Host = "localhost",
            Port = 5432,
            AdminUsername = "cluster_admin",
            AdminSecretRef = "secrets/admin",
        },
    };

    private static ISecretStore HappySecrets()
    {
        var store = Substitute.For<ISecretStore>();
        store.GetAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns("pw");
        return store;
    }

    [Fact]
    public async Task Missing_application_secret_fails_catalogue_sync_and_keeps_migration_metadata()
    {
        var row = NewRow();
        var provisioner = HappyProvisioner(row);
        var store = HappySecrets();
        store.GetAsync("secrets/app", Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult<string?>(null));
        var seeder = new TenantSeedService(Substitute.For<IModuleCatalog>());
        var tenantDbFactory = Substitute.For<ITenantDbContextFactory>();

        var ex = await Should.ThrowAsync<InvalidOperationException>(() =>
            TenantMigrationRunner.SyncCatalogueAsync(
                provisioner, store, seeder, tenantDbFactory, row, CancellationToken.None));
        ex.Message.ShouldContain("application secret");
        provisioner.DidNotReceive().BuildConnectionString(
            Arg.Any<DbCluster>(), row.DatabaseName, row.AppRoleName, Arg.Any<string>());

        // Through the runner, the same delegate Program.cs wires is a per-tenant
        // sync failure while migration metadata stays committed.
        var result = await TenantMigrationRunner.RunAsync(
            provisioner, store, row, "tenant 1008", "btree_gist", dryRun: false,
            (r, c) => TenantMigrationRunner.SyncCatalogueAsync(
                provisioner, store, seeder, tenantDbFactory, r, c),
            CancellationToken.None);
        result.CatalogueSyncFailed.ShouldBeTrue();
        row.SchemaVersion.ShouldBe("20240101_Init");
        row.LastMigratedAt.ShouldNotBeNull();
    }

    private static ITenantDatabaseProvisioner HappyProvisioner(TenantDatabase row)
    {
        var p = Substitute.For<ITenantDatabaseProvisioner>();
        p.BuildConnectionString(Arg.Any<DbCluster>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>())
            .Returns("cs");
        p.DatabaseExistsAsync(Arg.Any<ClusterConnection>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(true);
        p.VerifyMigratorConnectionAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(true);
        p.GetTenantIdentityAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((Guid?)row.TenantId);
        p.IsExtensionAvailableAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(true);
        p.GetAppliedMigrationsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<string>)new List<string> { "20240101_Init" });
        p.GetPendingMigrationsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<string>)new List<string> { "20240201_Next" });
        return p;
    }

    // ── Selector scenarios ─────────────────────────────────────────────────

    [Fact]
    public void Single_tenant_selector_resolves_tenant_number()
    {
        var opts = new Dictionary<string, string> { ["tenant"] = "1008" };

        TenantMigrationRunner.TryResolveSelector(opts, out var selector, out var error)
            .ShouldBeTrue();

        selector.TenantNumber.ShouldBe(1008);
        selector.TenantId.ShouldBeNull();
        selector.All.ShouldBeFalse();
        error.ShouldBeNull();
    }

    [Fact]
    public void Tenant_id_selector_resolves_guid()
    {
        var id = Guid.NewGuid();
        var opts = new Dictionary<string, string> { ["tenant-id"] = id.ToString() };

        TenantMigrationRunner.TryResolveSelector(opts, out var selector, out var error)
            .ShouldBeTrue();

        selector.TenantId.ShouldBe(id);
        selector.TenantNumber.ShouldBeNull();
        error.ShouldBeNull();
    }

    [Fact]
    public void Explicit_all_selector_is_required_for_multi_tenant_runs()
    {
        TenantMigrationRunner.TryResolveSelector(
            new Dictionary<string, string> { ["all"] = "true" },
            out var all, out _).ShouldBeTrue();
        all.All.ShouldBeTrue();

        // No selector is never implicit --all.
        TenantMigrationRunner.TryResolveSelector(
            new Dictionary<string, string>(), out _, out var noneError).ShouldBeFalse();
        noneError!.ShouldContain("exactly one");

        // Selectors are mutually exclusive.
        TenantMigrationRunner.TryResolveSelector(
            new Dictionary<string, string> { ["all"] = "true", ["tenant"] = "1008" },
            out _, out var bothError).ShouldBeFalse();
        bothError!.ShouldContain("exactly one");
    }

    [Fact]
    public void Invalid_selector_values_are_rejected()
    {
        TenantMigrationRunner.TryResolveSelector(
            new Dictionary<string, string> { ["tenant"] = "abc" }, out _, out _).ShouldBeFalse();
        TenantMigrationRunner.TryResolveSelector(
            new Dictionary<string, string> { ["tenant"] = "0" }, out _, out _).ShouldBeFalse();
        TenantMigrationRunner.TryResolveSelector(
            new Dictionary<string, string> { ["tenant"] = "-3" }, out _, out _).ShouldBeFalse();
        TenantMigrationRunner.TryResolveSelector(
            new Dictionary<string, string> { ["tenant-id"] = "not-a-guid" }, out _, out _).ShouldBeFalse();
    }

    // ── Per-tenant run scenarios ───────────────────────────────────────────

    [Fact]
    public async Task Dry_run_performs_preflight_only_and_no_writes()
    {
        var row = NewRow();
        var provisioner = HappyProvisioner(row);
        var store = HappySecrets();
        var syncInvoked = 0;
        Func<TenantDatabase, CancellationToken, Task> sync = (_, _) =>
        {
            Interlocked.Increment(ref syncInvoked);
            return Task.CompletedTask;
        };

        var result = await TenantMigrationRunner.RunAsync(
            provisioner, store, row, "1008 (acme)", "btree_gist",
            dryRun: true, sync, CancellationToken.None);

        result.DryRun.ShouldBeTrue();
        result.PendingCount.ShouldBe(1);
        syncInvoked.ShouldBe(0);
        await provisioner.DidNotReceive().MigrateAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        await provisioner.DidNotReceive().ApplyGrantsAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        row.LastMigratedAt.ShouldBeNull();
        row.SchemaVersion.ShouldBeNull();
    }

    [Fact]
    public async Task Migration_failure_stops_before_grants_and_leaves_metadata_untouched()
    {
        var row = NewRow();
        var provisioner = HappyProvisioner(row);
        provisioner.MigrateAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromException(new InvalidOperationException("migration boom")));

        await Should.ThrowAsync<InvalidOperationException>(() =>
            TenantMigrationRunner.RunAsync(
                provisioner, HappySecrets(), row, "t", "btree_gist",
                dryRun: false, null, CancellationToken.None));

        await provisioner.DidNotReceive().ApplyGrantsAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        row.LastMigratedAt.ShouldBeNull();
        row.SchemaVersion.ShouldBeNull();
    }

    [Fact]
    public async Task Missing_migrator_credentials_fails_before_connecting()
    {
        var row = NewRow();
        var provisioner = HappyProvisioner(row);
        var store = Substitute.For<ISecretStore>();
        store.GetAsync("secrets/admin", Arg.Any<CancellationToken>()).Returns("pw");
        store.GetAsync("secrets/migrator", Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult<string?>(null));

        var ex = await Should.ThrowAsync<InvalidOperationException>(() =>
            TenantMigrationRunner.RunAsync(
                provisioner, store, row, "t", "btree_gist",
                dryRun: false, null, CancellationToken.None));

        ex.Message.ShouldContain("Migrator secret");
        await provisioner.DidNotReceive().VerifyMigratorConnectionAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        await provisioner.DidNotReceive().MigrateAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Tenant_identity_mismatch_fails_before_migration()
    {
        var row = NewRow();
        var provisioner = HappyProvisioner(row);
        provisioner.GetTenantIdentityAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((Guid?)Guid.NewGuid());

        var ex = await Should.ThrowAsync<InvalidOperationException>(() =>
            TenantMigrationRunner.RunAsync(
                provisioner, HappySecrets(), row, "t", "btree_gist",
                dryRun: false, null, CancellationToken.None));

        ex.Message.ShouldContain("Tenant identity mismatch");
        await provisioner.DidNotReceive().MigrateAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        await provisioner.DidNotReceive().ApplyGrantsAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Retry_after_grant_failure_completes_safely()
    {
        var row = NewRow();
        var provisioner = HappyProvisioner(row);
        var grantCalls = 0;
        provisioner.ApplyGrantsAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(_ => Interlocked.Increment(ref grantCalls) == 1
                ? Task.FromException(new InvalidOperationException("grant boom"))
                : Task.CompletedTask);

        // First attempt: migration applied, grants fail → metadata untouched.
        await Should.ThrowAsync<InvalidOperationException>(() =>
            TenantMigrationRunner.RunAsync(
                provisioner, HappySecrets(), row, "t", "btree_gist",
                dryRun: false, null, CancellationToken.None));
        row.LastMigratedAt.ShouldBeNull();
        row.SchemaVersion.ShouldBeNull();

        // Retry: idempotent migrate no-ops, grants re-applied, metadata written.
        var syncInvoked = 0;
        Func<TenantDatabase, CancellationToken, Task> sync = (_, _) =>
        {
            Interlocked.Increment(ref syncInvoked);
            return Task.CompletedTask;
        };

        var result = await TenantMigrationRunner.RunAsync(
            provisioner, HappySecrets(), row, "t", "btree_gist",
            dryRun: false, sync, CancellationToken.None);

        grantCalls.ShouldBe(2);
        await provisioner.Received(2).MigrateAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        result.DryRun.ShouldBeFalse();
        result.CatalogueSyncFailed.ShouldBeFalse();
        row.LastMigratedAt.ShouldNotBeNull();
        row.SchemaVersion.ShouldBe("20240101_Init");
        syncInvoked.ShouldBe(1);
    }

    [Fact]
    public async Task Successful_run_updates_metadata_and_runs_catalogue_sync()
    {
        var row = NewRow();
        var provisioner = HappyProvisioner(row);
        var syncedRow = (TenantDatabase?)null;
        Func<TenantDatabase, CancellationToken, Task> sync = (r, _) =>
        {
            syncedRow = r;
            return Task.CompletedTask;
        };

        var result = await TenantMigrationRunner.RunAsync(
            provisioner, HappySecrets(), row, "t", "btree_gist",
            dryRun: false, sync, CancellationToken.None);

        result.DryRun.ShouldBeFalse();
        result.CatalogueSyncFailed.ShouldBeFalse();
        result.PendingCount.ShouldBe(1);
        await provisioner.Received(1).MigrateAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        await provisioner.Received(1).ApplyGrantsAsync("cs", "hrms_app_1008", Arg.Any<CancellationToken>());
        syncedRow.ShouldBe(row);
        row.LastMigratedAt.ShouldNotBeNull();
        row.SchemaVersion.ShouldBe("20240101_Init");
    }

    [Fact]
    public async Task Catalogue_sync_failure_is_reported_without_hiding_migration_success()
    {
        var row = NewRow();
        var provisioner = HappyProvisioner(row);
        Func<TenantDatabase, CancellationToken, Task> sync = (_, _) =>
            Task.FromException(new InvalidOperationException("sync boom"));

        var result = await TenantMigrationRunner.RunAsync(
            provisioner, HappySecrets(), row, "t", "btree_gist",
            dryRun: false, sync, CancellationToken.None);

        result.CatalogueSyncFailed.ShouldBeTrue();
        // The migration itself succeeded, so metadata stays set for reporting/retry.
        row.LastMigratedAt.ShouldNotBeNull();
        row.SchemaVersion.ShouldBe("20240101_Init");
    }
}
