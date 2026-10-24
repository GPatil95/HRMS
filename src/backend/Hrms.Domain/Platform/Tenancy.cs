using Hrms.Shared.Auditing;
using Hrms.Shared.Primitives;

namespace Hrms.Domain.Platform;

public enum TenantStatus
{
    Provisioning = 0,
    ProvisioningFailed = 1,
    Active = 2,
    Suspended = 3,
    Inactive = 4,
}

[Audited("platform.tenancy")]
public sealed class Tenant : AuditableEntity, IConcurrencyAware
{
    public int TenantNumber { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public TenantStatus Status { get; set; } = TenantStatus.Provisioning;
    public string Region { get; set; } = "local";
    public string TimeZone { get; set; } = "Asia/Kolkata";
    public string AdminEmail { get; set; } = string.Empty;
    public long EntitlementVersion { get; set; } = 1;
    public uint RowVersion { get; set; }

    public List<TenantDomain> Domains { get; set; } = [];

    public bool CanTransitionTo(TenantStatus target) => (Status, target) switch
    {
        (TenantStatus.Provisioning, TenantStatus.Active) => true,
        (TenantStatus.Provisioning, TenantStatus.ProvisioningFailed) => true,
        (TenantStatus.ProvisioningFailed, TenantStatus.Provisioning) => true,
        (TenantStatus.Active, TenantStatus.Suspended) => true,
        (TenantStatus.Active, TenantStatus.Inactive) => true,
        (TenantStatus.Suspended, TenantStatus.Active) => true,
        (TenantStatus.Suspended, TenantStatus.Inactive) => true,
        (TenantStatus.Inactive, TenantStatus.Active) => true,
        _ => false,
    };

    public static string DatabaseNameFor(int tenantNumber) => $"hrms_tenant_{tenantNumber}";
}

public sealed class TenantDomain : Entity
{
    public Guid TenantId { get; set; }
    public string HostName { get; set; } = string.Empty;
    public bool IsPrimary { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class DbCluster : AuditableEntity
{
    public string Name { get; set; } = string.Empty;
    public string Host { get; set; } = string.Empty;
    public int Port { get; set; } = 5432;
    public string AdminUsername { get; set; } = string.Empty;
    public string AdminSecretRef { get; set; } = string.Empty;
    public string Region { get; set; } = "local";
    public int MaxTenants { get; set; } = 300;
    public bool IsActive { get; set; } = true;
}

public enum TenantDatabaseStatus
{
    Provisioning = 0,
    Online = 1,
    Maintenance = 2,
    Offline = 3,
}

/// <summary>Tenant database registry. Stores secret references only — never credentials.</summary>
[Audited("platform.tenancy")]
public sealed class TenantDatabase : AuditableEntity, IConcurrencyAware
{
    public Guid TenantId { get; set; }
    public Guid ClusterId { get; set; }
    public string DatabaseName { get; set; } = string.Empty;
    public string AppRoleName { get; set; } = string.Empty;
    public string AppSecretRef { get; set; } = string.Empty;
    public string MigratorRoleName { get; set; } = string.Empty;
    public string MigratorSecretRef { get; set; } = string.Empty;
    public TenantDatabaseStatus Status { get; set; } = TenantDatabaseStatus.Provisioning;
    public string? SchemaVersion { get; set; }
    public DateTimeOffset? LastMigratedAt { get; set; }
    public DateTimeOffset? LastBackupAt { get; set; }
    public string? LastBackupRef { get; set; }
    public DateTimeOffset? ProvisionedAt { get; set; }
    public uint RowVersion { get; set; }

    public DbCluster? Cluster { get; set; }
}

public enum ProvisioningStatus
{
    Pending = 0,
    Running = 1,
    Succeeded = 2,
    Failed = 3,
}

public sealed class ProvisioningStepRecord
{
    public string Step { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public DateTimeOffset At { get; set; }
    public string? Error { get; set; }
}

public sealed class ProvisioningOperation : AuditableEntity, IConcurrencyAware
{
    public Guid TenantId { get; set; }
    public string Operation { get; set; } = "create";
    public ProvisioningStatus Status { get; set; } = ProvisioningStatus.Pending;
    public int Attempt { get; set; }
    public string? CurrentStep { get; set; }
    public List<ProvisioningStepRecord> Steps { get; set; } = [];
    public string? Error { get; set; }
    public string? CorrelationId { get; set; }
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public uint RowVersion { get; set; }

    public bool IsStepCompleted(string step) => Steps.Any(s => s.Step == step && s.Status == "completed");

    public void MarkStep(string step, string status, DateTimeOffset at, string? error = null)
    {
        Steps.RemoveAll(s => s.Step == step);
        Steps.Add(new ProvisioningStepRecord { Step = step, Status = status, At = at, Error = error });
        CurrentStep = step;
    }
}

[Audited("platform.config")]
public sealed class PlatformSetting : AuditableEntity
{
    public string Key { get; set; } = string.Empty;
    public string Value { get; set; } = "null";
    public string? Description { get; set; }
}

public sealed class SecretEntry
{
    public string Ref { get; set; } = string.Empty;
    public byte[] Ciphertext { get; set; } = [];
    public byte[] Nonce { get; set; } = [];
    public byte[] Tag { get; set; } = [];
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
}
