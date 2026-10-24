using System.Text.RegularExpressions;
using Hrms.Application.Abstractions;
using Hrms.Domain.Platform;
using Hrms.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Hrms.Infrastructure.Tenancy;

/// <summary>
/// Idempotent PostgreSQL provisioning: per-tenant migrator (owner) and app (DML-only) roles, CONNECT restricted to those roles,
/// EF migrations, grants, and the tenant identity row checked on every resolution.
/// </summary>
public sealed partial class PostgresTenantDatabaseProvisioner(IOptions<ProvisioningOptions> options) : ITenantDatabaseProvisioner
{
    public static readonly string[] Schemas = ["identity", "org", "files", "notify", "dashboard", "config", "audit", "ops"];

    [GeneratedRegex("^[a-z_][a-z0-9_]{0,62}$")]
    private static partial Regex Identifier();

    public async Task EnsureRoleAsync(ClusterConnection admin, string roleName, string password, CancellationToken ct)
    {
        Ident(roleName);
        await using var conn = await OpenAsync(Admin(admin, "postgres"), ct);
        var exists = await ScalarAsync<bool>(conn, "SELECT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = @n)", ct, ("n", roleName));
        var verb = exists ? "ALTER" : "CREATE";
        // Password is a URL-safe random token, but is still escaped as a literal.
        await ExecAsync(conn, $"{verb} ROLE \"{roleName}\" LOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE NOINHERIT PASSWORD '{password.Replace("'", "''", StringComparison.Ordinal)}'", ct);
    }

    public async Task EnsureDatabaseAsync(ClusterConnection admin, string databaseName, string ownerRole, CancellationToken ct)
    {
        Ident(databaseName);
        Ident(ownerRole);
        await using var conn = await OpenAsync(Admin(admin, "postgres"), ct);
        if (!await ScalarAsync<bool>(conn, "SELECT EXISTS (SELECT 1 FROM pg_database WHERE datname = @n)", ct, ("n", databaseName)))
        {
            await ExecAsync(conn, $"GRANT \"{ownerRole}\" TO CURRENT_USER", ct);
            await ExecAsync(conn, $"CREATE DATABASE \"{databaseName}\" OWNER \"{ownerRole}\" ENCODING 'UTF8' TEMPLATE template0", ct);
        }
    }

    public async Task ConfigureDatabaseAsync(ClusterConnection admin, string databaseName, string migratorRole, string appRole, CancellationToken ct)
    {
        Ident(databaseName);
        Ident(migratorRole);
        Ident(appRole);
        await using (var conn = await OpenAsync(Admin(admin, "postgres"), ct))
        {
            await ExecAsync(conn, $"REVOKE ALL ON DATABASE \"{databaseName}\" FROM PUBLIC", ct);
            await ExecAsync(conn, $"GRANT CONNECT, TEMPORARY ON DATABASE \"{databaseName}\" TO \"{appRole}\"", ct);
            await ExecAsync(conn, $"GRANT CONNECT, CREATE, TEMPORARY ON DATABASE \"{databaseName}\" TO \"{migratorRole}\"", ct);
        }

        await using var db = await OpenAsync(Admin(admin, databaseName), ct);
        await ExecAsync(db, "REVOKE ALL ON SCHEMA public FROM PUBLIC", ct);
        await ExecAsync(db, $"ALTER SCHEMA public OWNER TO \"{migratorRole}\"", ct);
    }

    public async Task MigrateAsync(string migratorConnectionString, CancellationToken ct)
    {
        await using var ctx = TenantDbContextFactory.Create(migratorConnectionString);
        await ctx.Database.MigrateAsync(ct);
    }

    public async Task ApplyGrantsAsync(string migratorConnectionString, string appRole, CancellationToken ct)
    {
        Ident(appRole);
        await using var conn = await OpenAsync(migratorConnectionString, ct);
        foreach (var s in Schemas)
        {
            await ExecAsync(conn, $"GRANT USAGE ON SCHEMA \"{s}\" TO \"{appRole}\"", ct);
            await ExecAsync(conn, $"GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA \"{s}\" TO \"{appRole}\"", ct);
            await ExecAsync(conn, $"GRANT USAGE, SELECT ON ALL SEQUENCES IN SCHEMA \"{s}\" TO \"{appRole}\"", ct);
            await ExecAsync(conn, $"ALTER DEFAULT PRIVILEGES IN SCHEMA \"{s}\" GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO \"{appRole}\"", ct);
        }

        // Audit tables are append-only for the application role (also enforced by triggers created in migrations).
        await ExecAsync(conn, $"REVOKE UPDATE, DELETE, TRUNCATE ON audit.audit_entries, audit.security_events, audit.audit_seals FROM \"{appRole}\"", ct);
        await ExecAsync(conn, $"REVOKE INSERT, UPDATE, DELETE, TRUNCATE ON ops.tenant_identity FROM \"{appRole}\"", ct);
        await ExecAsync(conn, $"GRANT SELECT ON \"__EFMigrationsHistory\" TO \"{appRole}\"", ct);
    }

    public async Task WriteTenantIdentityAsync(string migratorConnectionString, Guid tenantId, int tenantNumber, CancellationToken ct)
    {
        await using var conn = await OpenAsync(migratorConnectionString, ct);
        var existing = await ScalarAsync<Guid?>(conn, "SELECT tenant_id FROM ops.tenant_identity LIMIT 1", ct);
        if (existing is { } id && id != tenantId)
        {
            throw new InvalidOperationException("Tenant database is already bound to a different tenant.");
        }

        if (existing is null)
        {
            await ExecAsync(conn, "INSERT INTO ops.tenant_identity (tenant_id, tenant_number, created_at) VALUES (@t, @n, now())", ct, ("t", tenantId), ("n", tenantNumber));
        }
    }

    public async Task<bool> VerifyAsync(string appConnectionString, Guid tenantId, CancellationToken ct)
    {
        await using var conn = await OpenAsync(appConnectionString, ct);
        var ids = new List<Guid>();
        await using (var cmd = new NpgsqlCommand("SELECT tenant_id FROM ops.tenant_identity", conn))
        await using (var r = await cmd.ExecuteReaderAsync(ct))
        {
            while (await r.ReadAsync(ct))
            {
                ids.Add(r.GetGuid(0));
            }
        }

        return ids.Count == 1 && ids[0] == tenantId;
    }

    public async Task<IReadOnlyList<string>> GetAppliedMigrationsAsync(string connectionString, CancellationToken ct)
    {
        await using var ctx = TenantDbContextFactory.Create(connectionString);
        return [.. await ctx.Database.GetAppliedMigrationsAsync(ct)];
    }

    public async Task<IReadOnlyList<string>> GetPendingMigrationsAsync(string connectionString, CancellationToken ct)
    {
        await using var ctx = TenantDbContextFactory.Create(connectionString);
        return [.. await ctx.Database.GetPendingMigrationsAsync(ct)];
    }

    public string BuildConnectionString(DbCluster cluster, string database, string username, string password) => new NpgsqlConnectionStringBuilder
    {
        Host = cluster.Host,
        Port = cluster.Port,
        Database = database,
        Username = username,
        Password = password,
        SslMode = Enum.Parse<SslMode>(options.Value.SslMode, ignoreCase: true),
        MaxPoolSize = options.Value.MaxPoolSize,
        ApplicationName = "hrms",
    }.ConnectionString;

    private string Admin(ClusterConnection a, string database) =>
        BuildConnectionString(new DbCluster { Host = a.Host, Port = a.Port }, database, a.Username, a.Password);

    private static void Ident(string value)
    {
        if (!Identifier().IsMatch(value))
        {
            throw new ArgumentException("Invalid PostgreSQL identifier.", nameof(value));
        }
    }

    private static async Task<NpgsqlConnection> OpenAsync(string cs, CancellationToken ct)
    {
        var conn = new NpgsqlConnection(cs);
        await conn.OpenAsync(ct);
        return conn;
    }

    private static async Task ExecAsync(NpgsqlConnection conn, string sql, CancellationToken ct, params (string Name, object Value)[] ps)
    {
        await using var cmd = new NpgsqlCommand(sql, conn);
        foreach (var (n, v) in ps)
        {
            cmd.Parameters.AddWithValue(n, v);
        }

        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static async Task<T> ScalarAsync<T>(NpgsqlConnection conn, string sql, CancellationToken ct, params (string Name, object Value)[] ps)
    {
        await using var cmd = new NpgsqlCommand(sql, conn);
        foreach (var (n, v) in ps)
        {
            cmd.Parameters.AddWithValue(n, v);
        }

        var result = await cmd.ExecuteScalarAsync(ct);
        return result is null or DBNull ? default! : (T)result;
    }
}
