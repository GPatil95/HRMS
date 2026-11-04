using Hrms.Shared.Auditing;
using Hrms.Shared.Primitives;

namespace Hrms.Domain.Tenant;

public enum FileStatus
{
    Available = 0,
    Quarantined = 1,
    Rejected = 2,
}

[Audited("core.files")]
public sealed class FileObject : AuditableEntity, ISoftDeletable
{
    public string StorageKey { get; set; } = string.Empty;
    public string OriginalFileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
    public string Sha256 { get; set; } = string.Empty;
    public string Purpose { get; set; } = string.Empty;
    public string Classification { get; set; } = "internal";
    public string? OwnerEntityType { get; set; }
    public string? OwnerEntityId { get; set; }
    public FileStatus Status { get; set; }
    public bool IsDeleted { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }
    public Guid? DeletedBy { get; set; }
}

public sealed class FileAccessLog : Entity
{
    public Guid FileId { get; set; }
    public Guid? UserId { get; set; }
    public string Action { get; set; } = string.Empty;
    public DateTimeOffset OccurredAt { get; set; }
    public string? IpAddress { get; set; }
}

public enum NotificationPriority
{
    Low = 0,
    Normal = 1,
    High = 2,
    Critical = 3,
}

public sealed class Notification : Entity
{
    public Guid UserId { get; set; }
    public string Type { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string? Body { get; set; }
    public string? Link { get; set; }
    public NotificationPriority Priority { get; set; } = NotificationPriority.Normal;
    public bool IsRead { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? ReadAt { get; set; }
}

public static class NotificationChannels
{
    public const string InApp = "in_app";
    public const string Email = "email";
    public const string Push = "push";
    public const string Sms = "sms";
}

[Audited("core.notify")]
public sealed class NotificationTemplate : AuditableEntity
{
    public string Code { get; set; } = string.Empty;
    public string Channel { get; set; } = NotificationChannels.Email;
    public string Locale { get; set; } = "en";
    public string Subject { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    public int Version { get; set; } = 1;
    public bool IsActive { get; set; } = true;
    public bool IsSystem { get; set; }
}

public sealed class NotificationPreference : Entity
{
    public Guid UserId { get; set; }
    public string NotificationType { get; set; } = string.Empty;
    public string Channel { get; set; } = string.Empty;
    public bool Enabled { get; set; }
}

[Audited("core.dashboard")]
public sealed class Dashboard : AuditableEntity
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public bool IsSystem { get; set; }
    public List<DashboardWidget> Widgets { get; set; } = [];
}

public sealed class DashboardWidget : Entity
{
    public Guid DashboardId { get; set; }
    public string WidgetCode { get; set; } = string.Empty;
    public int X { get; set; }
    public int Y { get; set; }
    public int Width { get; set; } = 4;
    public int Height { get; set; } = 2;
    public string? Settings { get; set; }
}

public enum DashboardAssigneeType
{
    TenantDefault = 0,
    Role = 1,
    User = 2,
}

[Audited("core.dashboard")]
public sealed class DashboardAssignment : AuditableEntity
{
    public Guid DashboardId { get; set; }
    public DashboardAssigneeType AssigneeType { get; set; }
    public Guid? AssigneeId { get; set; }
}

[Audited("core.config")]
public sealed class TenantSetting : AuditableEntity
{
    public string Key { get; set; } = string.Empty;
    public string Value { get; set; } = "null";
}
