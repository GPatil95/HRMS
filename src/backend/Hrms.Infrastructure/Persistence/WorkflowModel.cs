using Hrms.Domain.Tenant;
using Microsoft.EntityFrameworkCore;

namespace Hrms.Infrastructure.Persistence;

/// <summary>EF mapping for the core.workflow (<c>workflow</c> schema) and core.workitems (<c>workitems</c>) tables.</summary>
internal static class WorkflowModel
{
    public static void Configure(ModelBuilder b)
    {
        b.Entity<WorkflowDefinition>(e =>
        {
            e.ToTable("definitions", "workflow", t =>
            {
                t.HasCheckConstraint("ck_definitions_status", "status in ('draft','published','retired')");
                t.HasCheckConstraint("ck_definitions_window", "effective_to is null or effective_from is null or effective_to >= effective_from");
                t.HasCheckConstraint("ck_definitions_amounts", "min_amount is null or max_amount is null or max_amount >= min_amount");
            });
            e.Property(x => x.Code).HasMaxLength(64);
            e.Property(x => x.Name).HasMaxLength(200);
            e.Property(x => x.DocumentType).HasMaxLength(64);
            e.Property(x => x.ModuleCode).HasMaxLength(64);
            e.Property(x => x.Status).HasMaxLength(16);
            e.Property(x => x.Description).HasMaxLength(1000);
            e.Property(x => x.MinAmount).HasPrecision(18, 2);
            e.Property(x => x.MaxAmount).HasPrecision(18, 2);
            e.HasIndex(x => new { x.Code, x.Version }).IsUnique();
            e.HasIndex(x => new { x.DocumentType, x.Status });
            e.HasMany(x => x.Steps).WithOne().HasForeignKey(s => s.DefinitionId).OnDelete(DeleteBehavior.Cascade);
        });
        b.Entity<WorkflowDefinitionStep>(e =>
        {
            e.ToTable("definition_steps", "workflow", t =>
            {
                t.HasCheckConstraint("ck_definition_steps_order", "step_order >= 1");
                t.HasCheckConstraint("ck_definition_steps_mode", "approval_mode in ('any','all')");
                t.HasCheckConstraint("ck_definition_steps_sla", "sla_hours is null or sla_hours > 0");
            });
            e.Property(x => x.Name).HasMaxLength(200);
            e.Property(x => x.ApproverType).HasMaxLength(32);
            e.Property(x => x.ApproverParam).HasMaxLength(128);
            e.Property(x => x.ApprovalMode).HasMaxLength(8);
            e.Property(x => x.EscalationType).HasMaxLength(32);
            e.Property(x => x.EscalationParam).HasMaxLength(128);
            e.Property(x => x.MinAmount).HasPrecision(18, 2);
            e.Property(x => x.MaxAmount).HasPrecision(18, 2);
            e.HasIndex(x => new { x.DefinitionId, x.StepOrder });
            e.HasOne<TenantUser>().WithMany().HasForeignKey(x => x.FallbackUserId).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<WorkflowInstance>(e =>
        {
            e.ToTable("instances", "workflow", t =>
                t.HasCheckConstraint("ck_instances_status", "status in ('in_progress','sent_back','approved','rejected','cancelled')"));
            e.Property(x => x.DocumentType).HasMaxLength(64);
            e.Property(x => x.DocumentId).HasMaxLength(128);
            e.Property(x => x.ModuleCode).HasMaxLength(64);
            e.Property(x => x.Title).HasMaxLength(200);
            e.Property(x => x.DeepLink).HasMaxLength(512);
            e.Property(x => x.Status).HasMaxLength(16);
            e.Property(x => x.ContextJson).HasColumnType("jsonb");
            e.Property(x => x.Amount).HasPrecision(18, 2);
            e.HasOne<WorkflowDefinition>().WithMany().HasForeignKey(x => x.DefinitionId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<TenantUser>().WithMany().HasForeignKey(x => x.RequesterUserId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Employee>().WithMany().HasForeignKey(x => x.SubjectEmployeeId).OnDelete(DeleteBehavior.Restrict);
            // One live workflow per document.
            e.HasIndex(x => new { x.DocumentType, x.DocumentId }).IsUnique()
                .HasFilter("status in ('in_progress','sent_back')").HasDatabaseName("ux_instances_active_document");
            e.HasIndex(x => new { x.RequesterUserId, x.Status });
            e.HasIndex(x => new { x.Status, x.StartedAt });
        });
        b.Entity<WorkflowInstanceStep>(e =>
        {
            e.ToTable("instance_steps", "workflow", t =>
                t.HasCheckConstraint("ck_instance_steps_status", "status in ('pending','approved','rejected','sent_back','superseded','cancelled')"));
            e.Property(x => x.Name).HasMaxLength(200);
            e.Property(x => x.ApprovalMode).HasMaxLength(8);
            e.Property(x => x.Status).HasMaxLength(16);
            e.Property(x => x.Comment).HasMaxLength(2000);
            e.HasOne<WorkflowInstance>().WithMany().HasForeignKey(x => x.InstanceId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<WorkflowDefinitionStep>().WithMany().HasForeignKey(x => x.DefinitionStepId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<TenantUser>().WithMany().HasForeignKey(x => x.AssigneeUserId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => new { x.InstanceId, x.StepOrder });
            e.HasIndex(x => new { x.AssigneeUserId, x.Status });
            e.HasIndex(x => new { x.Status, x.DueAt });
        });
        b.Entity<WorkflowAction>(e =>
        {
            e.ToTable("actions", "workflow");
            e.Property(x => x.Action).HasMaxLength(32);
            e.Property(x => x.Comment).HasMaxLength(2000);
            e.Property(x => x.IdempotencyKey).HasMaxLength(128);
            e.HasOne<WorkflowInstance>().WithMany().HasForeignKey(x => x.InstanceId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => new { x.InstanceId, x.OccurredAt });
            e.HasIndex(x => new { x.InstanceId, x.IdempotencyKey }).IsUnique().HasFilter("idempotency_key is not null");
        });
        b.Entity<WorkflowDelegation>(e =>
        {
            e.ToTable("delegations", "workflow", t =>
            {
                t.HasCheckConstraint("ck_delegations_window", "valid_to >= valid_from");
                t.HasCheckConstraint("ck_delegations_not_self", "delegator_user_id <> delegate_user_id");
            });
            e.Property(x => x.Reason).HasMaxLength(500);
            e.HasOne<TenantUser>().WithMany().HasForeignKey(x => x.DelegatorUserId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<TenantUser>().WithMany().HasForeignKey(x => x.DelegateUserId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => new { x.DelegatorUserId, x.ValidFrom });
        });
        b.Entity<WorkflowOutboxEvent>(e =>
        {
            e.ToTable("outbox_events", "workflow");
            e.Property(x => x.EventType).HasMaxLength(64);
            e.Property(x => x.DocumentType).HasMaxLength(64);
            e.Property(x => x.DocumentId).HasMaxLength(128);
            e.Property(x => x.Status).HasMaxLength(16);
            e.Property(x => x.PayloadJson).HasColumnType("jsonb");
            e.Property(x => x.LastError).HasMaxLength(2000);
            e.HasIndex(x => x.OccurredAt).HasFilter("processed_at is null").HasDatabaseName("ix_outbox_events_pending");
        });
        b.Entity<WorkItem>(e =>
        {
            e.ToTable("work_items", "workitems", t =>
                t.HasCheckConstraint("ck_work_items_status", "status in ('open','completed','cancelled')"));
            e.Property(x => x.ModuleCode).HasMaxLength(64);
            e.Property(x => x.ItemType).HasMaxLength(64);
            e.Property(x => x.SourceRef).HasMaxLength(128);
            e.Property(x => x.Title).HasMaxLength(200);
            e.Property(x => x.Priority).HasMaxLength(16);
            e.Property(x => x.Status).HasMaxLength(16);
            e.Property(x => x.DeepLink).HasMaxLength(512);
            e.HasOne<TenantUser>().WithMany().HasForeignKey(x => x.AssigneeUserId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => new { x.ItemType, x.SourceRef, x.AssigneeUserId }).IsUnique();
            e.HasIndex(x => new { x.AssigneeUserId, x.Status, x.DueAt });
            e.HasIndex(x => x.WorkflowInstanceId);
        });
    }
}
