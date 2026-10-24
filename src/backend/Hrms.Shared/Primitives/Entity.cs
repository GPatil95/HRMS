namespace Hrms.Shared.Primitives;

public abstract class Entity
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
}

public interface IAuditableEntity
{
    DateTimeOffset CreatedAt { get; set; }
    Guid? CreatedBy { get; set; }
    DateTimeOffset? ModifiedAt { get; set; }
    Guid? ModifiedBy { get; set; }
}

public abstract class AuditableEntity : Entity, IAuditableEntity
{
    public DateTimeOffset CreatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public DateTimeOffset? ModifiedAt { get; set; }
    public Guid? ModifiedBy { get; set; }
}

public interface ISoftDeletable
{
    bool IsDeleted { get; set; }
    DateTimeOffset? DeletedAt { get; set; }
    Guid? DeletedBy { get; set; }
}

/// <summary>Marker for entities whose optimistic concurrency token is PostgreSQL xmin.</summary>
public interface IConcurrencyAware
{
    uint RowVersion { get; set; }
}
