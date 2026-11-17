namespace Hrms.Shared.Auditing;

/// <summary>Entity changes are written to the audit trail in the same transaction.</summary>
[AttributeUsage(AttributeTargets.Class, Inherited = true)]
public sealed class AuditedAttribute(string moduleCode) : Attribute
{
    public string ModuleCode { get; } = moduleCode;
}

/// <summary>Property is never written to the audit trail (e.g. password hashes, secrets).</summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class AuditIgnoreAttribute : Attribute;

/// <summary>Property is recorded as changed, but its values are masked.</summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class AuditMaskedAttribute : Attribute;
