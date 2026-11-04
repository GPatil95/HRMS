using Hrms.Domain.Common;
using Hrms.Shared.Primitives;
using Microsoft.EntityFrameworkCore;

namespace Hrms.Infrastructure.Persistence;

internal static class CommonModel
{
    /// <summary>Audit, security event, seal, email outbox and job tables shared by both realms.</summary>
    public static void ConfigureShared(ModelBuilder b)
    {
        b.Entity<AuditEntry>(e =>
        {
            e.ToTable("audit_entries", "audit");
            e.HasKey(x => x.Id);
            e.Property(x => x.ModuleCode).HasMaxLength(64);
            e.Property(x => x.EntityType).HasMaxLength(128);
            e.Property(x => x.EntityId).HasMaxLength(256);
            e.Property(x => x.Action).HasMaxLength(64);
            e.Property(x => x.ActorType).HasMaxLength(32);
            e.Property(x => x.ActorDisplay).HasMaxLength(256);
            e.Property(x => x.Source).HasMaxLength(32);
            e.Property(x => x.IpAddress).HasMaxLength(64);
            e.Property(x => x.UserAgent).HasMaxLength(512);
            e.Property(x => x.CorrelationId).HasMaxLength(64);
            e.Property(x => x.Reason).HasMaxLength(1000);
            e.Property(x => x.ContentHash).HasMaxLength(64);
            e.Property(x => x.Changes).HasColumnType("jsonb");
            e.Property(x => x.Before).HasColumnType("jsonb");
            e.Property(x => x.After).HasColumnType("jsonb");
            e.HasIndex(x => x.OccurredAt);
            e.HasIndex(x => new { x.EntityType, x.EntityId });
            e.HasIndex(x => x.ActorId);
            e.HasIndex(x => x.CorrelationId);
            e.ToTable(t => t.HasCheckConstraint("ck_audit_entries_source", "source IN ('web','mobile','api','import','job','integration','ai','system','support')"));
        });

        b.Entity<SecurityEvent>(e =>
        {
            e.ToTable("security_events", "audit");
            e.HasKey(x => x.Id);
            e.Property(x => x.EventType).HasMaxLength(64);
            e.Property(x => x.UserEmail).HasMaxLength(256);
            e.Property(x => x.IpAddress).HasMaxLength(64);
            e.Property(x => x.UserAgent).HasMaxLength(512);
            e.Property(x => x.CorrelationId).HasMaxLength(64);
            e.Property(x => x.Details).HasColumnType("jsonb");
            e.Property(x => x.ContentHash).HasMaxLength(64);
            e.HasIndex(x => x.OccurredAt);
            e.HasIndex(x => new { x.EventType, x.OccurredAt });
            e.HasIndex(x => x.UserId);
        });

        b.Entity<AuditSeal>(e =>
        {
            e.ToTable("audit_seals", "audit");
            e.HasKey(x => x.Id);
            e.Property(x => x.PreviousSealHash).HasMaxLength(64);
            e.Property(x => x.ChainHash).HasMaxLength(64);
            e.HasIndex(x => x.RangeEnd).IsUnique();
        });

        b.Entity<EmailMessage>(e =>
        {
            e.ToTable("email_messages", "ops");
            e.HasKey(x => x.Id);
            e.Property(x => x.Subject).HasMaxLength(500);
            e.Property(x => x.TemplateCode).HasMaxLength(128);
            e.Property(x => x.LastError).HasMaxLength(1000);
            e.Property(x => x.ProviderMessageId).HasMaxLength(256);
            e.Property(x => x.CorrelationId).HasMaxLength(64);
            e.HasIndex(x => new { x.Status, x.ScheduledAt });
            e.ToTable(t => t.HasCheckConstraint("ck_email_messages_attempts", "attempts >= 0"));
        });

        b.Entity<JobExecution>(e =>
        {
            e.ToTable("job_executions", "ops");
            e.HasKey(x => x.Id);
            e.Property(x => x.JobType).HasMaxLength(128);
            e.Property(x => x.IdempotencyKey).HasMaxLength(256);
            e.Property(x => x.Error).HasMaxLength(2000);
            e.Property(x => x.CorrelationId).HasMaxLength(64);
            e.HasIndex(x => x.IdempotencyKey).IsUnique().HasFilter("idempotency_key IS NOT NULL");
            e.HasIndex(x => new { x.JobType, x.StartedAt });
        });
    }

    /// <summary>Applies xmin concurrency tokens and string length defaults by convention.</summary>
    public static void ApplyConventions(ModelBuilder b)
    {
        foreach (var et in b.Model.GetEntityTypes())
        {
            if (typeof(IConcurrencyAware).IsAssignableFrom(et.ClrType))
            {
                b.Entity(et.ClrType).Property(nameof(IConcurrencyAware.RowVersion)).IsRowVersion();
            }

            foreach (var p in et.GetProperties().Where(p => p.ClrType == typeof(string) && p.GetMaxLength() is null && p.GetColumnType() is null))
            {
                p.SetMaxLength(p.Name is "Body" or "HtmlBody" or "TextBody" or "Value" or "Settings" or "Description" ? null : 256);
            }
        }
    }

    public static void ConfigureIdentityUser<T>(ModelBuilder b, string schema, string table) where T : IdentityUserBase
    {
        b.Entity<T>(e =>
        {
            e.ToTable(table, schema, t => t.HasCheckConstraint($"ck_{table}_status", "status BETWEEN 0 AND 4"));
            e.HasKey(x => x.Id);
            e.Property(x => x.Email).HasMaxLength(256);
            e.HasIndex(x => x.Email).IsUnique();
            e.Property(x => x.DisplayName).HasMaxLength(200);
            e.Property(x => x.PasswordHash).HasMaxLength(512);
            e.Property(x => x.SecurityStamp).HasMaxLength(64);
            e.Property(x => x.MfaSecretProtected).HasMaxLength(1024);
        });
    }

    public static void ConfigureSession<T>(ModelBuilder b, string schema, string table) where T : SessionBase
    {
        b.Entity<T>(e =>
        {
            e.ToTable(table, schema);
            e.HasKey(x => x.Id);
            e.Property(x => x.Client).HasMaxLength(32);
            e.Property(x => x.IpAddress).HasMaxLength(64);
            e.Property(x => x.UserAgent).HasMaxLength(512);
            e.Property(x => x.AuthMethods).HasMaxLength(64);
            e.Property(x => x.RevokedReason).HasMaxLength(128);
            e.HasIndex(x => new { x.UserId, x.RevokedAt });
        });
    }

    public static void ConfigureToken<T>(ModelBuilder b, string schema, string table) where T : UserTokenBase
    {
        b.Entity<T>(e =>
        {
            e.ToTable(table, schema);
            e.HasKey(x => x.Id);
            e.Property(x => x.Purpose).HasMaxLength(32);
            e.Property(x => x.TokenHash).HasMaxLength(128);
            e.HasIndex(x => x.TokenHash).IsUnique();
            e.HasIndex(x => new { x.UserId, x.Purpose });
        });
    }
}
