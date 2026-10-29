using Hrms.Application.Tenancy;
using Hrms.Infrastructure;
using Hrms.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

// Operator tooling. Secrets (passwords, master key) are read from environment variables only — never from arguments.
//   migrate-platform
//   seed-platform  --admin-email <e> --admin-name <n> --cluster-host <h> --cluster-port <p> --cluster-user <u>
//                  env: HRMS_ADMIN_PASSWORD, HRMS_CLUSTER_ADMIN_PASSWORD
//   create-tenant  --code <c> --name <n> --admin-email <e> --admin-name <n> [--plan <code>]
//   retry-tenant   --id <tenantId>
var command = args.FirstOrDefault() ?? "help";
var opts = ParseOptions(args.Skip(1).ToArray());

var builder = Host.CreateApplicationBuilder();
builder.Configuration.AddInMemoryCollection([new("Jobs:Mode", "InProcess")]);
builder.Configuration.AddEnvironmentVariables();
builder.Services.AddHrmsInfrastructure(builder.Configuration);
using var host = builder.Build();
await host.StartAsync();
var jobQueue = host.Services.GetRequiredService<Hrms.Infrastructure.Jobs.InProcessJobQueue>();
using var scope = host.Services.CreateScope();
var sp = scope.ServiceProvider;
var ct = CancellationToken.None;

switch (command)
{
    case "migrate-platform":
        await sp.GetRequiredService<PlatformDbContext>().Database.MigrateAsync(ct);
        Console.WriteLine("Platform database migrated.");
        return 0;

    case "migrate-tenants":
    {
        // Applies pending tenant-schema migrations to every provisioned (Online) tenant database.
        var pdb = sp.GetRequiredService<PlatformDbContext>();
        var provisioner = sp.GetRequiredService<Hrms.Application.Abstractions.ITenantDatabaseProvisioner>();
        var store = sp.GetRequiredService<Hrms.Application.Abstractions.ISecretStore>();
        var rows = await pdb.TenantDatabases.Include(r => r.Cluster)
            .Where(r => r.Status == Hrms.Domain.Platform.TenantDatabaseStatus.Online).ToListAsync(ct);
        var migrated = 0;
        foreach (var row in rows)
        {
            var password = await store.GetAsync(row.MigratorSecretRef, ct);
            if (password is null)
            {
                Console.Error.WriteLine($"{row.DatabaseName}: migrator secret missing - skipped.");
                continue;
            }

            var conn = provisioner.BuildConnectionString(row.Cluster!, row.DatabaseName, row.MigratorRoleName, password);
            var pending = await provisioner.GetPendingMigrationsAsync(conn, ct);
            if (pending.Count == 0)
            {
                Console.WriteLine($"{row.DatabaseName}: up to date.");
                continue;
            }

            await provisioner.MigrateAsync(conn, ct);
            row.LastMigratedAt = DateTimeOffset.UtcNow;
            row.SchemaVersion = (await provisioner.GetAppliedMigrationsAsync(conn, ct)).LastOrDefault();
            migrated++;
            Console.WriteLine($"{row.DatabaseName}: applied {pending.Count} migration(s) -> {row.SchemaVersion}.");
        }

        await pdb.SaveChangesAsync(ct);
        Console.WriteLine(migrated == 0 && rows.Count == 0 ? "No online tenant databases." : $"Migrated {migrated} tenant database(s).");
        return 0;
    }

    case "seed-platform":
    {
        var seed = sp.GetRequiredService<PlatformSeedService>();
        await seed.SeedAsync(ct);
        await seed.EnsureClusterAsync("default", Req(opts, "cluster-host"), int.Parse(opts.GetValueOrDefault("cluster-port", "5432"), System.Globalization.CultureInfo.InvariantCulture),
            Req(opts, "cluster-user"), Env("HRMS_CLUSTER_ADMIN_PASSWORD"), ct);
        await seed.EnsurePlatformAdminAsync(Req(opts, "admin-email"), opts.GetValueOrDefault("admin-name", "Platform Admin"), Env("HRMS_ADMIN_PASSWORD"), ct);
        Console.WriteLine("Platform catalog, cluster and super admin seeded.");
        return 0;
    }

    case "create-tenant":
    {
        var svc = sp.GetRequiredService<TenantProvisioningService>();
        var r = await svc.CreateTenantAsync(new CreateTenantCommand(Req(opts, "code"), Req(opts, "name"), Req(opts, "admin-email"), Req(opts, "admin-name"), opts.GetValueOrDefault("plan"), opts.GetValueOrDefault("admin-password")), ct);
        if (!r.IsSuccess)
        {
            Console.Error.WriteLine($"{r.Error!.Code}: {r.Error.Message}");
            return 1;
        }

        var status = await svc.RunAsync(r.Value.OperationId, ct);
        await jobQueue.DrainAsync(TimeSpan.FromSeconds(30));
        Console.WriteLine($"Tenant {r.Value.TenantId} provisioning: {status}");
        return status == Hrms.Domain.Platform.ProvisioningStatus.Succeeded ? 0 : 1;
    }

    case "retry-tenant":
    {
        var svc = sp.GetRequiredService<TenantProvisioningService>();
        var r = await svc.PrepareRetryAsync(Guid.Parse(Req(opts, "id")), ct);
        if (!r.IsSuccess)
        {
            Console.Error.WriteLine($"{r.Error!.Code}: {r.Error.Message}");
            return 1;
        }

        var status = await svc.RunAsync(r.Value, ct);
        await jobQueue.DrainAsync(TimeSpan.FromSeconds(30));
        Console.WriteLine($"Provisioning: {status}");
        return status == Hrms.Domain.Platform.ProvisioningStatus.Succeeded ? 0 : 1;
    }

    case "initialize-master-key":
    {
        // Idempotent first-time setup: creates the key file only if absent. Never overwrites an existing key.
        var path = Hrms.Infrastructure.Security.PlatformMasterKeyProvider.ResolveKeyFileTarget(builder.Configuration);
        if (Hrms.Infrastructure.Security.PlatformMasterKeyProvider.InitializeKeyFile(path))
        {
            Console.WriteLine($"Master key initialized: {path}");
        }
        else
        {
            Console.WriteLine($"MASTER KEY ALREADY EXISTS: {path}");
        }

        return 0;
    }

    case "verify-master-key":
    {
        // Safe diagnostic: source + validity + short fingerprint. Never prints key material.
        try
        {
            var info = sp.GetRequiredService<Hrms.Application.Abstractions.IPlatformMasterKeyProvider>().Key;
            Console.WriteLine($"Master key:\n  Source: {info.Source}\n  Valid: YES\n  Fingerprint: {info.Fingerprint}");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Master key:\n  Valid: NO\n  Error: {ex.Message}");
            return 1;
        }
    }

    case "rotate-master-key":
    {
        // Explicit, atomic rotation — requires a file-based key (Secrets:MasterKeyFile or /keys/master.key).
        // 1. decrypt every secret under the current key (abort on ANY failure)
        // 2. re-encrypt all under the new key, verify each, persist rows
        // 3. swap the key file (write temp + rename)
        if (!string.IsNullOrWhiteSpace(builder.Configuration["Secrets:MasterKey"]))
        {
            Console.Error.WriteLine("rotate-master-key requires a file-based master key (Secrets:MasterKeyFile); an inline Secrets:MasterKey cannot be rotated by this command.");
            return 1;
        }

        var info = sp.GetRequiredService<Hrms.Application.Abstractions.IPlatformMasterKeyProvider>().Key;
        var db = sp.GetRequiredService<PlatformDbContext>();
        var rows = await db.Secrets.ToListAsync(ct);

        // Step 1 — validate all secrets decrypt under the CURRENT key before touching anything.
        var plaintext = new Dictionary<Hrms.Domain.Platform.SecretEntry, string>();
        var failed = new List<string>();
        foreach (var row in rows)
        {
            try { plaintext[row] = await sp.GetRequiredService<Hrms.Application.Abstractions.ISecretStore>().GetAsync(row.Ref, ct) ?? string.Empty; }
            catch (System.Security.Cryptography.AuthenticationTagMismatchException) { failed.Add(row.Ref); }
        }

        if (failed.Count > 0)
        {
            Console.Error.WriteLine($"ROTATION ABORTED — {failed.Count} secret(s) do not decrypt under the current key:");
            foreach (var r in failed) Console.Error.WriteLine($"  {r}");
            Console.Error.WriteLine("Fix or remove the undecryptable secrets first (see repair-secrets). No changes were made.");
            return 1;
        }

        var newKey = opts.TryGetValue("new-key", out var nk) ? Convert.FromBase64String(nk) : System.Security.Cryptography.RandomNumberGenerator.GetBytes(32);
        if (newKey.Length != 32)
        {
            Console.Error.WriteLine("--new-key must be 32 bytes (base64).");
            return 1;
        }

        // Step 2 — re-encrypt under the new key and verify each row before committing.
        foreach (var (row, plain) in plaintext)
        {
            var nonce = System.Security.Cryptography.RandomNumberGenerator.GetBytes(12);
            var plainBytes = System.Text.Encoding.UTF8.GetBytes(plain);
            var cipher = new byte[plainBytes.Length];
            var tag = new byte[16];
            var ad = System.Text.Encoding.UTF8.GetBytes(row.Ref);
            using (var aes = new System.Security.Cryptography.AesGcm(newKey, 16)) aes.Encrypt(nonce, plainBytes, cipher, tag, ad);
            // verify round-trip under the new key
            using (var aes = new System.Security.Cryptography.AesGcm(newKey, 16))
            {
                var roundTrip = new byte[plainBytes.Length];
                aes.Decrypt(nonce, cipher, tag, roundTrip, ad);
                if (!roundTrip.SequenceEqual(plainBytes)) throw new InvalidOperationException($"Rotation verification failed for {row.Ref}");
            }

            row.Ciphertext = cipher; row.Nonce = nonce; row.Tag = tag; row.UpdatedAt = DateTimeOffset.UtcNow;
        }

        // Step 3 — persist rows, then atomically swap the key file.
        var keyPath = Hrms.Infrastructure.Security.PlatformMasterKeyProvider.ResolveKeyFileTarget(builder.Configuration);
        var tmp = keyPath + ".new";
        File.WriteAllText(tmp, Convert.ToBase64String(newKey));
        await db.SaveChangesAsync(ct);
        File.Move(tmp, keyPath, overwrite: true);
        Console.WriteLine($"Master key rotated: {plaintext.Count} secret(s) re-encrypted, key file updated ({keyPath}). New fingerprint: {Hrms.Infrastructure.Security.PlatformMasterKeyProvider.Fingerprint(newKey)}");
        Console.WriteLine("Restart the API so it loads the new key.");
        return 0;
    }

    case "rekey":
    {
        // Re-encrypt every ops.secrets row from a previous master key to the currently configured one.
        // Use after rotating Secrets:MasterKey/MasterKeyFile so existing ciphertext keeps decrypting.
        var oldKey = Convert.FromBase64String(Req(opts, "old-key"));
        if (oldKey.Length != 32)
        {
            Console.Error.WriteLine("--old-key must be 32 bytes (base64).");
            return 1;
        }

        var db = sp.GetRequiredService<PlatformDbContext>();
        var store = sp.GetRequiredService<Hrms.Application.Abstractions.ISecretStore>();
        var rows = await db.Secrets.ToListAsync(ct);
        var migrated = 0;
        var skipped = 0;
        foreach (var row in rows)
        {
            try
            {
                using var aes = new System.Security.Cryptography.AesGcm(oldKey, 16);
                var plain = new byte[row.Ciphertext.Length];
                aes.Decrypt(row.Nonce, row.Ciphertext, row.Tag, plain, System.Text.Encoding.UTF8.GetBytes(row.Ref));
                await store.SetAsync(row.Ref, System.Text.Encoding.UTF8.GetString(plain), ct);
                migrated++;
            }
            catch (System.Security.Cryptography.AuthenticationTagMismatchException)
            {
                skipped++;
                Console.Error.WriteLine($"skipped (not encrypted with --old-key): {row.Ref}");
            }
        }

        Console.WriteLine($"Rekeyed {migrated} secret(s); {skipped} skipped.");
        return skipped > 0 && migrated == 0 ? 1 : 0;
    }

    case "repair-secrets":
    {
        // Verifies every ops.secrets row decrypts under the authoritative configured master key.
        // Default mode is non-destructive: it reports undecryptable rows and never rewrites ciphertext.
        // --fix is the explicit lost-key recovery: it regenerates ONLY the undecryptable rows —
        // re-stores the cluster admin secret and re-issues tenant app/migrator role passwords
        // via ALTER ROLE (requires HRMS_CLUSTER_ADMIN_PASSWORD). Tenant data is untouched.
        var db = sp.GetRequiredService<PlatformDbContext>();
        var store = sp.GetRequiredService<Hrms.Application.Abstractions.ISecretStore>();
        var provisioner = sp.GetRequiredService<Hrms.Application.Abstractions.ITenantDatabaseProvisioner>();
        var rows = await db.Secrets.ToListAsync(ct);
        var broken = new List<Hrms.Domain.Platform.SecretEntry>();
        foreach (var row in rows)
        {
            try { _ = await store.GetAsync(row.Ref, ct); }
            catch (System.Security.Cryptography.AuthenticationTagMismatchException)
            {
                broken.Add(row);
                Console.Error.WriteLine($"Secret {row.Ref} could not be decrypted using the configured platform master key.");
            }
        }

        if (broken.Count == 0)
        {
            Console.WriteLine($"All {rows.Count} secret(s) decrypt under the configured master key.");
            return 0;
        }

        var fix = opts.ContainsKey("fix");
        if (!fix)
        {
            Console.Error.WriteLine($"{broken.Count} secret(s) are undecryptable. Existing ciphertext was NOT modified.");
            Console.Error.WriteLine("If the previous master key is recoverable, rotate it back in via rekey --old-key instead.");
            Console.Error.WriteLine("If it is permanently lost, re-run with --fix (requires HRMS_CLUSTER_ADMIN_PASSWORD) to regenerate the affected role secrets.");
            return 1;
        }

        var adminPassword = Env("HRMS_CLUSTER_ADMIN_PASSWORD");
        var cluster = opts.TryGetValue("cluster", out var cname)
            ? await db.DbClusters.FirstOrDefaultAsync(c => c.Name == cname, ct) ?? throw new InvalidOperationException($"Cluster '{cname}' not found.")
            : await db.DbClusters.Where(c => c.IsActive).OrderBy(c => c.CreatedAt).FirstOrDefaultAsync(ct) ?? throw new InvalidOperationException("No active cluster.");

        if (opts.TryGetValue("cluster-host", out var h)) { cluster.Host = h; }
        if (opts.TryGetValue("cluster-port", out var p)) { cluster.Port = int.Parse(p, System.Globalization.CultureInfo.InvariantCulture); }
        if (opts.TryGetValue("cluster-user", out var u)) { cluster.AdminUsername = u; }

        var brokenRefs = broken.Select(b => b.Ref).ToHashSet(StringComparer.Ordinal);
        var fixedCount = 0;
        string? adminPasswordForRoles = null;
        if (brokenRefs.Contains(cluster.AdminSecretRef))
        {
            await store.SetAsync(cluster.AdminSecretRef, adminPassword, ct);
            Console.WriteLine($"Cluster '{cluster.Name}' admin secret re-encrypted under the current master key.");
            fixedCount++;
        }

        var tenants = await db.TenantDatabases.Include(d => d.Cluster).Where(d => d.ClusterId == cluster.Id).ToListAsync(ct);
        foreach (var t in tenants)
        {
            var repairMigrator = brokenRefs.Contains(t.MigratorSecretRef);
            var repairApp = brokenRefs.Contains(t.AppSecretRef);
            if (!repairMigrator && !repairApp)
            {
                continue;
            }

            adminPasswordForRoles ??= await store.GetAsync(cluster.AdminSecretRef, ct);
            var admin = new Hrms.Application.Abstractions.ClusterConnection(cluster.Host, cluster.Port, cluster.AdminUsername, adminPasswordForRoles!);
            if (repairMigrator)
            {
                var pw = Hrms.Application.Common.SecureTokens.CreatePassword();
                await provisioner.EnsureRoleAsync(admin, t.MigratorRoleName, pw, ct);
                await store.SetAsync(t.MigratorSecretRef, pw, ct);
            }

            if (repairApp)
            {
                var pw = Hrms.Application.Common.SecureTokens.CreatePassword();
                await provisioner.EnsureRoleAsync(admin, t.AppRoleName, pw, ct);
                await store.SetAsync(t.AppSecretRef, pw, ct);
            }

            fixedCount += (repairMigrator ? 1 : 0) + (repairApp ? 1 : 0);
            Console.WriteLine($"Regenerated undecryptable role secrets for {t.DatabaseName}");
        }

        var unrepaired = brokenRefs.Count - fixedCount;
        Console.WriteLine($"Repair complete: {fixedCount} secret(s) regenerated under the current master key.");
        if (unrepaired > 0)
        {
            Console.Error.WriteLine($"{unrepaired} secret(s) could not be regenerated automatically (non-role secrets). Their ciphertext was preserved.");
            return 1;
        }

        return 0;
    }

    default:
        Console.WriteLine("Commands: migrate-platform | migrate-tenants | seed-platform | create-tenant | retry-tenant | initialize-master-key | verify-master-key | repair-secrets [--fix] | rekey | rotate-master-key");
        return command == "help" ? 0 : 1;
}

static Dictionary<string, string> ParseOptions(string[] a)
{
    var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    for (var i = 0; i < a.Length; i++)
    {
        var key = a[i].TrimStart('-');
        // Lone token (no value following, or next arg is another flag) → boolean-style flag.
        if (i + 1 >= a.Length || a[i + 1].StartsWith('-'))
        {
            d[key] = "true";
        }
        else
        {
            d[key] = a[++i];
        }
    }

    return d;
}

static string Req(Dictionary<string, string> o, string key) => o.TryGetValue(key, out var v) ? v : throw new ArgumentException($"--{key} is required.");

static string Env(string name) => Environment.GetEnvironmentVariable(name) is { Length: > 0 } v ? v : throw new InvalidOperationException($"Environment variable {name} is required.");
