using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hrms.Infrastructure.Persistence.Migrations.Tenant
{
    /// <inheritdoc />
    public partial class CoreWorkflow : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "workflow");

            migrationBuilder.EnsureSchema(
                name: "workitems");

            migrationBuilder.CreateTable(
                name: "definitions",
                schema: "workflow",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    document_type = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    module_code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    effective_from = table.Column<DateOnly>(type: "date", nullable: true),
                    effective_to = table.Column<DateOnly>(type: "date", nullable: true),
                    priority = table.Column<int>(type: "integer", nullable: false),
                    min_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    max_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    company_id = table.Column<Guid>(type: "uuid", nullable: true),
                    branch_id = table.Column<Guid>(type: "uuid", nullable: true),
                    department_id = table.Column<Guid>(type: "uuid", nullable: true),
                    published_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    modified_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    modified_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_definitions", x => x.id);
                    table.CheckConstraint("ck_definitions_amounts", "min_amount is null or max_amount is null or max_amount >= min_amount");
                    table.CheckConstraint("ck_definitions_status", "status in ('draft','published','retired')");
                    table.CheckConstraint("ck_definitions_window", "effective_to is null or effective_from is null or effective_to >= effective_from");
                });

            migrationBuilder.CreateTable(
                name: "delegations",
                schema: "workflow",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    delegator_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    delegate_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    valid_from = table.Column<DateOnly>(type: "date", nullable: false),
                    valid_to = table.Column<DateOnly>(type: "date", nullable: false),
                    reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    modified_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    modified_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_delegations", x => x.id);
                    table.CheckConstraint("ck_delegations_not_self", "delegator_user_id <> delegate_user_id");
                    table.CheckConstraint("ck_delegations_window", "valid_to >= valid_from");
                    table.ForeignKey(
                        name: "fk_delegations_users_delegate_user_id",
                        column: x => x.delegate_user_id,
                        principalSchema: "identity",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_delegations_users_delegator_user_id",
                        column: x => x.delegator_user_id,
                        principalSchema: "identity",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "outbox_events",
                schema: "workflow",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    event_type = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    instance_id = table.Column<Guid>(type: "uuid", nullable: false),
                    document_type = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    document_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    payload_json = table.Column<string>(type: "jsonb", nullable: true),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    processed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    attempts = table.Column<int>(type: "integer", nullable: false),
                    last_error = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_outbox_events", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "work_items",
                schema: "workitems",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    module_code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    item_type = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    source_ref = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    workflow_instance_id = table.Column<Guid>(type: "uuid", nullable: true),
                    assignee_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    due_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    priority = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    deep_link = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_work_items", x => x.id);
                    table.CheckConstraint("ck_work_items_status", "status in ('open','completed','cancelled')");
                    table.ForeignKey(
                        name: "fk_work_items_users_assignee_user_id",
                        column: x => x.assignee_user_id,
                        principalSchema: "identity",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "definition_steps",
                schema: "workflow",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    definition_id = table.Column<Guid>(type: "uuid", nullable: false),
                    step_order = table.Column<int>(type: "integer", nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    approver_type = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    approver_param = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    approver_scope = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false, defaultValue: "tenant"),
                    approval_mode = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    min_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    max_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    fallback_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    sla_hours = table.Column<int>(type: "integer", nullable: true),
                    escalation_type = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    escalation_param = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    allow_send_back = table.Column<bool>(type: "boolean", nullable: false),
                    allow_delegate = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    modified_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    modified_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_definition_steps", x => x.id);
                    table.CheckConstraint("ck_definition_steps_mode", "approval_mode in ('any','all')");
                    table.CheckConstraint("ck_definition_steps_order", "step_order >= 1");
                    table.CheckConstraint("ck_definition_steps_scope", "approver_scope in ('tenant','company','branch','department','reporting')");
                    table.CheckConstraint("ck_definition_steps_sla", "sla_hours is null or sla_hours > 0");
                    table.ForeignKey(
                        name: "fk_definition_steps_definitions_definition_id",
                        column: x => x.definition_id,
                        principalSchema: "workflow",
                        principalTable: "definitions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_definition_steps_users_fallback_user_id",
                        column: x => x.fallback_user_id,
                        principalSchema: "identity",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "instances",
                schema: "workflow",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    definition_id = table.Column<Guid>(type: "uuid", nullable: false),
                    document_type = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    document_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    module_code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    deep_link = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    requester_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    subject_employee_id = table.Column<Guid>(type: "uuid", nullable: true),
                    amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    context_json = table.Column<string>(type: "jsonb", nullable: true),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    current_step_order = table.Column<int>(type: "integer", nullable: false),
                    started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_action_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    modified_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    modified_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_instances", x => x.id);
                    table.CheckConstraint("ck_instances_status", "status in ('in_progress','sent_back','stalled','approved','rejected','cancelled')");
                    table.ForeignKey(
                        name: "fk_instances_definitions_definition_id",
                        column: x => x.definition_id,
                        principalSchema: "workflow",
                        principalTable: "definitions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_instances_employees_subject_employee_id",
                        column: x => x.subject_employee_id,
                        principalSchema: "org",
                        principalTable: "employees",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_instances_users_requester_user_id",
                        column: x => x.requester_user_id,
                        principalSchema: "identity",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "actions",
                schema: "workflow",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    instance_id = table.Column<Guid>(type: "uuid", nullable: false),
                    instance_step_id = table.Column<Guid>(type: "uuid", nullable: true),
                    action = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    actor_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    target_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    comment = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    idempotency_key = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_actions", x => x.id);
                    table.ForeignKey(
                        name: "fk_actions_instances_instance_id",
                        column: x => x.instance_id,
                        principalSchema: "workflow",
                        principalTable: "instances",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "instance_steps",
                schema: "workflow",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    instance_id = table.Column<Guid>(type: "uuid", nullable: false),
                    definition_step_id = table.Column<Guid>(type: "uuid", nullable: false),
                    step_order = table.Column<int>(type: "integer", nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    approval_mode = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    assignee_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    delegated_from_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    claimed_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    due_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    reminder_sent_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    escalation_level = table.Column<int>(type: "integer", nullable: false),
                    acted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    acted_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    comment = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    modified_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    modified_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_instance_steps", x => x.id);
                    table.CheckConstraint("ck_instance_steps_status", "status in ('pending','approved','rejected','sent_back','superseded','cancelled')");
                    table.ForeignKey(
                        name: "fk_instance_steps_definition_steps_definition_step_id",
                        column: x => x.definition_step_id,
                        principalSchema: "workflow",
                        principalTable: "definition_steps",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_instance_steps_instances_instance_id",
                        column: x => x.instance_id,
                        principalSchema: "workflow",
                        principalTable: "instances",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_instance_steps_users_assignee_user_id",
                        column: x => x.assignee_user_id,
                        principalSchema: "identity",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_actions_instance_id_idempotency_key",
                schema: "workflow",
                table: "actions",
                columns: new[] { "instance_id", "idempotency_key" },
                unique: true,
                filter: "idempotency_key is not null");

            migrationBuilder.CreateIndex(
                name: "ix_actions_instance_id_occurred_at",
                schema: "workflow",
                table: "actions",
                columns: new[] { "instance_id", "occurred_at" });

            migrationBuilder.CreateIndex(
                name: "ix_definition_steps_definition_id_step_order",
                schema: "workflow",
                table: "definition_steps",
                columns: new[] { "definition_id", "step_order" });

            migrationBuilder.CreateIndex(
                name: "ix_definition_steps_fallback_user_id",
                schema: "workflow",
                table: "definition_steps",
                column: "fallback_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_definitions_code_version",
                schema: "workflow",
                table: "definitions",
                columns: new[] { "code", "version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_definitions_document_type_status",
                schema: "workflow",
                table: "definitions",
                columns: new[] { "document_type", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_delegations_delegate_user_id",
                schema: "workflow",
                table: "delegations",
                column: "delegate_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_delegations_delegator_user_id_valid_from",
                schema: "workflow",
                table: "delegations",
                columns: new[] { "delegator_user_id", "valid_from" });

            migrationBuilder.CreateIndex(
                name: "ix_instance_steps_assignee_user_id_status",
                schema: "workflow",
                table: "instance_steps",
                columns: new[] { "assignee_user_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_instance_steps_definition_step_id",
                schema: "workflow",
                table: "instance_steps",
                column: "definition_step_id");

            migrationBuilder.CreateIndex(
                name: "ix_instance_steps_instance_id_step_order",
                schema: "workflow",
                table: "instance_steps",
                columns: new[] { "instance_id", "step_order" });

            migrationBuilder.CreateIndex(
                name: "ix_instance_steps_status_due_at",
                schema: "workflow",
                table: "instance_steps",
                columns: new[] { "status", "due_at" });

            migrationBuilder.CreateIndex(
                name: "ix_instances_definition_id",
                schema: "workflow",
                table: "instances",
                column: "definition_id");

            migrationBuilder.CreateIndex(
                name: "ix_instances_requester_user_id_status",
                schema: "workflow",
                table: "instances",
                columns: new[] { "requester_user_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_instances_status_started_at",
                schema: "workflow",
                table: "instances",
                columns: new[] { "status", "started_at" });

            migrationBuilder.CreateIndex(
                name: "ix_instances_subject_employee_id",
                schema: "workflow",
                table: "instances",
                column: "subject_employee_id");

            migrationBuilder.CreateIndex(
                name: "ux_instances_active_document",
                schema: "workflow",
                table: "instances",
                columns: new[] { "document_type", "document_id" },
                unique: true,
                filter: "status in ('in_progress','sent_back','stalled')");

            migrationBuilder.CreateIndex(
                name: "ix_outbox_events_pending",
                schema: "workflow",
                table: "outbox_events",
                column: "occurred_at",
                filter: "processed_at is null");

            migrationBuilder.CreateIndex(
                name: "ix_work_items_assignee_user_id_status_due_at",
                schema: "workitems",
                table: "work_items",
                columns: new[] { "assignee_user_id", "status", "due_at" });

            migrationBuilder.CreateIndex(
                name: "ix_work_items_item_type_source_ref_assignee_user_id",
                schema: "workitems",
                table: "work_items",
                columns: new[] { "item_type", "source_ref", "assignee_user_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_work_items_workflow_instance_id",
                schema: "workitems",
                table: "work_items",
                column: "workflow_instance_id");

            // Effective dating (btree_gist enabled by AttendanceMasters): one published version per definition code per day.
            migrationBuilder.Sql("""
                alter table workflow.definitions
                add constraint ex_definitions_published_no_overlap
                exclude using gist (
                  code with =,
                  daterange(effective_from, coalesce(effective_to, 'infinity'::date), '[]') with &&
                ) where (status = 'published');
                """);

            // One active out-of-office delegation per delegator per day.
            migrationBuilder.Sql("""
                alter table workflow.delegations
                add constraint ex_delegations_no_overlap
                exclude using gist (
                  delegator_user_id with =,
                  daterange(valid_from, valid_to, '[]') with &&
                ) where (is_active);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("alter table workflow.definitions drop constraint ex_definitions_published_no_overlap;");
            migrationBuilder.Sql("alter table workflow.delegations drop constraint ex_delegations_no_overlap;");

            migrationBuilder.DropTable(
                name: "actions",
                schema: "workflow");

            migrationBuilder.DropTable(
                name: "delegations",
                schema: "workflow");

            migrationBuilder.DropTable(
                name: "instance_steps",
                schema: "workflow");

            migrationBuilder.DropTable(
                name: "outbox_events",
                schema: "workflow");

            migrationBuilder.DropTable(
                name: "work_items",
                schema: "workitems");

            migrationBuilder.DropTable(
                name: "definition_steps",
                schema: "workflow");

            migrationBuilder.DropTable(
                name: "instances",
                schema: "workflow");

            migrationBuilder.DropTable(
                name: "definitions",
                schema: "workflow");
        }
    }
}
