using Hrms.Application.Abstractions;
using Hrms.Shared.Primitives;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Hrms.Infrastructure.Persistence;

public static class DbOptions
{
    public static void Configure(DbContextOptionsBuilder b, string cs, string migrationsTable = "__EFMigrationsHistory") =>
        b.UseNpgsql(cs, o => o.MigrationsHistoryTable(migrationsTable).EnableRetryOnFailure(3)).UseSnakeCaseNamingConvention();
}

/// <summary>Creates tenant contexts for an explicit connection (provisioning/migration only). Runtime code uses the scoped context.</summary>
public static class TenantDbContextFactory
{
    public static TenantDbContext Create(string cs, AuditRuntime? runtime = null)
    {
        var b = new DbContextOptionsBuilder<TenantDbContext>();
        DbOptions.Configure(b, cs);
        return new TenantDbContext(b.Options, runtime ?? new AuditRuntime(null, null, null, null, new SystemClock()));
    }
}

public sealed class TenantDbContextConnectionFactory(AuditRuntime runtime) : ITenantDbContextFactory
{
    public ITenantDbContext CreateForConnection(string connectionString) => TenantDbContextFactory.Create(connectionString, runtime);
}

public sealed class PlatformDesignTimeFactory : IDesignTimeDbContextFactory<PlatformDbContext>
{
    public PlatformDbContext CreateDbContext(string[] args)
    {
        var b = new DbContextOptionsBuilder<PlatformDbContext>();
        DbOptions.Configure(b, Environment.GetEnvironmentVariable("HRMS_DESIGN_PLATFORM_CS") ?? "Host=localhost;Database=hrms_platform_design;Username=postgres;Password=postgres");
        return new PlatformDbContext(b.Options, new AuditRuntime(null, null, null, null, new SystemClock()));
    }
}

public sealed class TenantDesignTimeFactory : IDesignTimeDbContextFactory<TenantDbContext>
{
    public TenantDbContext CreateDbContext(string[] args) =>
        TenantDbContextFactory.Create(Environment.GetEnvironmentVariable("HRMS_DESIGN_TENANT_CS") ?? "Host=localhost;Database=hrms_tenant_design;Username=postgres;Password=postgres");
}
