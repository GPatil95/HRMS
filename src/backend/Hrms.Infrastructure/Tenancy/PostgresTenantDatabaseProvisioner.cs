
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
    public static readonly string[] Schemas = ["identity", "org", "hr", "files", "notify", "dashboard", "config", "audit", "ops"];

    [GeneratedRegex("^[a-z_][a-z0-9_]{0,62}$")]
    private static partial Regex Identifier();

    public async Task EnsureRoleAsync(ClusterConnection admin, string roleName, string password, CancellationToken ct)
    {
        Ident(roleName);
        await using var conn = await OpenAsync(Admin(admin, "postgres"), ct);
        var exists = await ScalarAsync<bool>(conn, "SELECT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = @n)", ct, ("n", roleName));
        var verb = exists ? "ALTER" : "CREATE";
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

    // -----------------------------------------------------------------
    // Read-only migration preflight checks
    // -----------------------------------------------------------------

    public async Task<bool> DatabaseExistsAsync(
        ClusterConnection admin,
        string databaseName,
        CancellationToken ct)
    {
        Ident(databaseName);

        await using var conn = await OpenAsync(Admin(admin, "postgres"), ct);

        return await ScalarAsync<bool>(
            conn,
            "SELECT EXISTS (SELECT 1 FROM pg_database WHERE datname = @n)",
            ct,
            ("n", databaseName));
    }

    public async Task<bool> VerifyMigratorConnectionAsync(
        string connectionString,
        string expectedRole,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(expectedRole))
        {
            throw new ArgumentException(
                "Expected migrator role is required.",
                nameof(expectedRole));
        }

        Ident(expectedRole);

        await using var conn = await OpenAsync(connectionString, ct);

        return await ScalarAsync<bool>(
            conn,
            """
            SELECT current_user = @expectedRole
               AND session_user = @expectedRole
            """,
            ct,
            ("expectedRole", expectedRole));
    }

    public async Task<Guid?> GetTenantIdentityAsync(
        string connectionString,
        CancellationToken ct)
    {
        await using var conn = await OpenAsync(connectionString, ct);

        var tableExists = await ScalarAsync<bool>(
            conn,
            """
            SELECT EXISTS (
                SELECT 1
                FROM information_schema.tables
                WHERE table_schema = 'ops'
                  AND table_name = 'tenant_identity'
            )
            """,
            ct);

        if (!tableExists)
            return null;

        await using var cmd = new NpgsqlCommand(
            "SELECT tenant_id FROM ops.tenant_identity",
            conn);

        await using var reader = await cmd.ExecuteReaderAsync(ct);

        Guid? tenantId = null;

        while (await reader.ReadAsync(ct))
        {
            if (tenantId.HasValue)
            {
                throw new InvalidOperationException(
                    "Tenant identity table contains multiple rows.");
            }

            tenantId = reader.GetGuid(0);
        }

        return tenantId;
    }

    public async Task<bool> IsExtensionAvailableAsync(
        string connectionString,
        string extensionName,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(extensionName) ||
            !Regex.IsMatch(extensionName, "^[a-z_][a-z0-9_]{0,62}$"))
        {
            throw new ArgumentException(
                "Invalid PostgreSQL extension name.",
                nameof(extensionName));
        }

        await using var conn = await OpenAsync(connectionString, ct);

        return await ScalarAsync<bool>(
            conn,
            """
            SELECT EXISTS (
                SELECT 1
                FROM pg_available_extensions
                WHERE name = @name
            )
            """,
            ct,
            ("name", extensionName));
    }

    // -----------------------------------------------------------------
    // Migration and permissions
    // -----------------------------------------------------------------

    public async Task MigrateAsync(string migratorConnectionString, CancellationToken ct)
    {
        await using var ctx = TenantDbContextFactory.Create(migratorConnectionString);
        await ctx.Database.MigrateAsync(ct);
    }

    public async Task ApplyGrantsAsync(
        string migratorConnectionString,
        string appRole,
        CancellationToken ct)
    {
        Ident(appRole);
        await using var conn = await OpenAsync(migratorConnectionString, ct);

        // Validate protected tables before changing privileges.
        var requiredTables = new[]
        {
            ("audit", "audit_entries"),
            ("audit", "security_events"),
            ("audit", "audit_seals"),
            ("ops", "tenant_identity"),
            ("public", "__EFMigrationsHistory")
        };

        foreach (var (schema, table) in requiredTables)
        {
            if (!await TableExistsAsync(conn, schema, table, ct))
            {
                throw new InvalidOperationException(
                    $"Cannot apply tenant grants: required table {schema}.{table} does not exist.");
            }
        }

        // Resolve existing schemas before opening the transaction. All permission
        // changes below then run atomically.
        var existingSchemas = new List<string>();
        foreach (var schema in Schemas)
        {
            var exists = await ScalarAsync<bool>(
                conn,
                "SELECT EXISTS (SELECT 1 FROM information_schema.schemata WHERE schema_name = @schema)",
                ct,
                ("schema", schema));

            if (exists)
                existingSchemas.Add(schema);
        }

        await using var tx = await conn.BeginTransactionAsync(ct);
        try
        {
            foreach (var schema in existingSchemas)
            {
                await ExecInTransactionAsync(
                    conn, tx,
                    $"GRANT USAGE ON SCHEMA \"{schema}\" TO \"{appRole}\"",
                    ct);
await ExecInTransactionAsync(
    conn, tx,
    $"ALTER DEFAULT PRIVILEGES IN SCHEMA \"{schema}\" GRANT USAGE, SELECT ON SEQUENCES TO \"{appRole}\"",
    ct);

                if (schema == "audit")
                {
                    // Existing audit tables are append-only for the application role.
                    await ExecInTransactionAsync(
                        conn, tx,
                        $"GRANT SELECT, INSERT ON ALL TABLES IN SCHEMA \"{schema}\" TO \"{appRole}\"",
                        ct);

                    await ExecInTransactionAsync(
                        conn, tx,
                        $"REVOKE UPDATE, DELETE, TRUNCATE ON ALL TABLES IN SCHEMA \"{schema}\" FROM \"{appRole}\"",
                        ct);

                    // Future audit tables created by the migrator role remain append-only.
                    await ExecInTransactionAsync(
                        conn, tx,
                        $"ALTER DEFAULT PRIVILEGES IN SCHEMA \"{schema}\" REVOKE UPDATE, DELETE, TRUNCATE ON TABLES FROM \"{appRole}\"",
                        ct);

                    await ExecInTransactionAsync(
                        conn, tx,
                        $"ALTER DEFAULT PRIVILEGES IN SCHEMA \"{schema}\" GRANT SELECT, INSERT ON TABLES TO \"{appRole}\"",
                        ct);
                }
                else
                {
                    // Normal application schemas retain CRUD access.
                    await ExecInTransactionAsync(
                        conn, tx,
                        $"GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA \"{schema}\" TO \"{appRole}\"",
                        ct);

                    await ExecInTransactionAsync(
                        conn, tx,
                        $"ALTER DEFAULT PRIVILEGES IN SCHEMA \"{schema}\" GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO \"{appRole}\"",
                        ct);
                }

                await ExecInTransactionAsync(
                    conn, tx,
                    $"GRANT USAGE, SELECT ON ALL SEQUENCES IN SCHEMA \"{schema}\" TO \"{appRole}\"",
                    ct);
            }

            // Tenant identity is readable but cannot be changed by the application.
            await ExecInTransactionAsync(
                conn, tx,
                $"REVOKE INSERT, UPDATE, DELETE, TRUNCATE ON ops.tenant_identity FROM \"{appRole}\"",
                ct);

            // Public schema usage is needed to read the migration history table.
            await ExecInTransactionAsync(
                conn, tx,
                $"GRANT USAGE ON SCHEMA public TO \"{appRole}\"",
                ct);

            // Migration history is read-only for the application role.
            await ExecInTransactionAsync(
                conn, tx,
                $"REVOKE INSERT, UPDATE, DELETE, TRUNCATE ON public.\"__EFMigrationsHistory\" FROM \"{appRole}\"",
                ct);

            await ExecInTransactionAsync(
                conn, tx,
                $"GRANT SELECT ON public.\"__EFMigrationsHistory\" TO \"{appRole}\"",
                ct);

            await tx.CommitAsync(ct);
        }
        catch
        {
            await tx.RollbackAsync(CancellationToken.None);
            throw;
        }
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

    private static async Task<bool> TableExistsAsync(
        NpgsqlConnection conn,
        string schema,
        string table,
        CancellationToken ct)
    {
        return await ScalarAsync<bool>(
            conn,
            """
            SELECT EXISTS (
                SELECT 1
                FROM information_schema.tables
                WHERE table_schema = @schema
                  AND table_name = @table
            )
            """,
            ct,
            ("schema", schema),
            ("table", table));
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

    private static async Task ExecInTransactionAsync(
        NpgsqlConnection conn,
        NpgsqlTransaction transaction,
        string sql,
        CancellationToken ct)
    {
        await using var cmd = new NpgsqlCommand(sql, conn, transaction);
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
