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
        Console.WriteLine($"Provisioning: {status}");
        return status == Hrms.Domain.Platform.ProvisioningStatus.Succeeded ? 0 : 1;
    }

    default:
        Console.WriteLine("Commands: migrate-platform | seed-platform | create-tenant | retry-tenant");
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
