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
        var r = await svc.CreateTenantAsync(new CreateTenantCommand(Req(opts, "code"), Req(opts, "name"), Req(opts, "admin-email"), Req(opts, "admin-name"), opts.GetValueOrDefault("plan")), ct);
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
        // Recovery when the old master key is LOST: re-stores the cluster admin secret under the current key,
        // then regenerates every tenant's app/migrator role passwords (ALTER ROLE) and re-encrypts them.
        // Tenant data is untouched; existing tenant databases and roles are kept.
        var db = sp.GetRequiredService<PlatformDbContext>();
        var store = sp.GetRequiredService<Hrms.Application.Abstractions.ISecretStore>();
        var provisioner = sp.GetRequiredService<Hrms.Application.Abstractions.ITenantDatabaseProvisioner>();
        var adminPassword = Env("HRMS_CLUSTER_ADMIN_PASSWORD");

        var cluster = opts.TryGetValue("cluster", out var cname)
            ? await db.DbClusters.FirstOrDefaultAsync(c => c.Name == cname, ct) ?? throw new InvalidOperationException($"Cluster '{cname}' not found.")
            : await db.DbClusters.Where(c => c.IsActive).OrderBy(c => c.CreatedAt).FirstOrDefaultAsync(ct) ?? throw new InvalidOperationException("No active cluster.");

        if (opts.TryGetValue("cluster-host", out var h)) { cluster.Host = h; }
        if (opts.TryGetValue("cluster-port", out var p)) { cluster.Port = int.Parse(p, System.Globalization.CultureInfo.InvariantCulture); }
        if (opts.TryGetValue("cluster-user", out var u)) { cluster.AdminUsername = u; }

        await store.SetAsync(cluster.AdminSecretRef, adminPassword, ct);
        var admin = new Hrms.Application.Abstractions.ClusterConnection(cluster.Host, cluster.Port, cluster.AdminUsername, adminPassword);
        Console.WriteLine($"Cluster '{cluster.Name}' admin secret re-encrypted under the current master key.");

        var tenants = await db.TenantDatabases.Include(d => d.Cluster).Where(d => d.ClusterId == cluster.Id).ToListAsync(ct);
        foreach (var t in tenants)
        {
            var migratorPw = Hrms.Application.Common.SecureTokens.CreatePassword();
            var appPw = Hrms.Application.Common.SecureTokens.CreatePassword();
            await provisioner.EnsureRoleAsync(admin, t.MigratorRoleName, migratorPw, ct);
            await provisioner.EnsureRoleAsync(admin, t.AppRoleName, appPw, ct);
            await store.SetAsync(t.MigratorSecretRef, migratorPw, ct);
            await store.SetAsync(t.AppSecretRef, appPw, ct);
            Console.WriteLine($"Repaired role secrets for {t.DatabaseName}");
        }

        Console.WriteLine($"Repair complete: cluster admin + {tenants.Count} tenant(s) re-secured under the current master key.");
        return 0;
    }

    default:
        Console.WriteLine("Commands: migrate-platform | seed-platform | create-tenant | retry-tenant | rekey | repair-secrets");
        return command == "help" ? 0 : 1;
}

static Dictionary<string, string> ParseOptions(string[] a)
{
    var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    for (var i = 0; i + 1 < a.Length; i += 2)
    {
        d[a[i].TrimStart('-')] = a[i + 1];
    }

    return d;
}

static string Req(Dictionary<string, string> o, string key) => o.TryGetValue(key, out var v) ? v : throw new ArgumentException($"--{key} is required.");

static string Env(string name) => Environment.GetEnvironmentVariable(name) is { Length: > 0 } v ? v : throw new InvalidOperationException($"Environment variable {name} is required.");
