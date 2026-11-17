using Hrms.Shared.Auditing;
using Hrms.Shared.Primitives;

namespace Hrms.Domain.Platform;

public enum ModuleKind
{
    Core = 0,
    Optional = 1,
    SubModule = 2,
}

public sealed class CatalogModule : AuditableEntity
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public ModuleKind Kind { get; set; }
    public string? ParentCode { get; set; }
    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;
}

public enum DependencyKind
{
    Required = 0,
    Optional = 1,
    Integration = 2,
}

public sealed class ModuleDependency
{
    public Guid ModuleId { get; set; }
    public Guid DependsOnModuleId { get; set; }
    public DependencyKind Kind { get; set; }
}

public sealed class CatalogFeature : AuditableEntity
{
    public Guid ModuleId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string ValueType { get; set; } = "boolean";
    public string? DefaultValue { get; set; }
}

public enum PlanStatus
{
    Draft = 0,
    Published = 1,
    Retired = 2,
}

[Audited("platform.catalog")]
public sealed class SubscriptionPlan : AuditableEntity, IConcurrencyAware
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public int Version { get; set; } = 1;
    public PlanStatus Status { get; set; } = PlanStatus.Draft;
    public bool IsPublic { get; set; } = true;
    public int? EmployeeLimit { get; set; }
    public uint RowVersion { get; set; }
    public List<PlanModule> Modules { get; set; } = [];
    public List<PlanFeature> Features { get; set; } = [];
}

public sealed class PlanModule
{
    public Guid PlanId { get; set; }
    public Guid ModuleId { get; set; }
}

public sealed class PlanFeature
{
    public Guid PlanId { get; set; }
    public Guid FeatureId { get; set; }
    public string? Value { get; set; }
}

public enum SubscriptionStatus
{
    Trial = 0,
    Active = 1,
    PastDue = 2,
    Grace = 3,
    Suspended = 4,
    Cancelled = 5,
    Expired = 6,
}

[Audited("platform.subscription")]
public sealed class TenantSubscription : AuditableEntity, IConcurrencyAware
{
    public Guid TenantId { get; set; }
    public Guid PlanId { get; set; }
    public SubscriptionStatus Status { get; set; }
    public DateOnly StartsOn { get; set; }
    public DateOnly? EndsOn { get; set; }
    public DateOnly? TrialEndsOn { get; set; }
    public DateOnly? GraceEndsOn { get; set; }
    public int? EmployeeLimit { get; set; }
    public uint RowVersion { get; set; }

    public static readonly SubscriptionStatus[] UsableStatuses =
        [SubscriptionStatus.Trial, SubscriptionStatus.Active, SubscriptionStatus.PastDue, SubscriptionStatus.Grace];

    public bool IsUsable(DateOnly today) =>
        UsableStatuses.Contains(Status) && StartsOn <= today && (EndsOn is null || EndsOn >= today);
}

public enum EntitlementSource
{
    Plan = 0,
    AddOn = 1,
    Trial = 2,
    Manual = 3,
}

[Audited("platform.subscription")]
public sealed class TenantModuleEntitlement : AuditableEntity
{
    public Guid TenantId { get; set; }
    public Guid ModuleId { get; set; }
    public EntitlementSource Source { get; set; }
    public bool IsEnabled { get; set; } = true;
    public DateOnly ValidFrom { get; set; }
    public DateOnly? ValidTo { get; set; }
    public string? Reason { get; set; }
}

[Audited("platform.subscription")]
public sealed class TenantFeatureEntitlement : AuditableEntity
{
    public Guid TenantId { get; set; }
    public Guid FeatureId { get; set; }
    public string? Value { get; set; }
    public EntitlementSource Source { get; set; }
    public bool IsEnabled { get; set; } = true;
    public DateOnly ValidFrom { get; set; }
    public DateOnly? ValidTo { get; set; }
    public string? Reason { get; set; }
}
