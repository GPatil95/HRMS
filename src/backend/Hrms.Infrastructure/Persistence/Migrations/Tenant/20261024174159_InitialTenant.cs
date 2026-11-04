using Hrms.Infrastructure.Persistence;
﻿using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hrms.Infrastructure.Persistence.Migrations.Tenant
{
    /// <inheritdoc />
    public partial class InitialTenant : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "identity");

            migrationBuilder.EnsureSchema(
                name: "audit");

            migrationBuilder.EnsureSchema(
                name: "org");

            migrationBuilder.EnsureSchema(
                name: "dashboard");

            migrationBuilder.EnsureSchema(
                name: "ops");

            migrationBuilder.EnsureSchema(
                name: "files");

            migrationBuilder.EnsureSchema(
                name: "notify");

            migrationBuilder.EnsureSchema(
                name: "config");

            migrationBuilder.CreateTable(
                name: "audit_entries",
                schema: "audit",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: true),
                    module_code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    entity_type = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    entity_id = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    action = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    actor_type = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    actor_id = table.Column<Guid>(type: "uuid", nullable: true),
                    actor_display = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    on_behalf_of = table.Column<Guid>(type: "uuid", nullable: true),
                    changes = table.Column<string>(type: "jsonb", nullable: true),
                    before = table.Column<string>(type: "jsonb", nullable: true),
                    after = table.Column<string>(type: "jsonb", nullable: true),
                    source = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ip_address = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    user_agent = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    correlation_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    content_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_audit_entries", x => x.id);
                    table.CheckConstraint("ck_audit_entries_source", "source IN ('web','mobile','api','import','job','integration','ai','system','support')");
                });

            migrationBuilder.CreateTable(
                name: "audit_seals",
                schema: "audit",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    sealed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    range_start = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    range_end = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    entry_count = table.Column<int>(type: "integer", nullable: false),
                    previous_seal_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    chain_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_audit_seals", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "authorization_state",
                schema: "identity",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_authorization_state", x => x.id);
                    table.CheckConstraint("ck_authorization_state_singleton", "id = 1");
                });

            migrationBuilder.CreateTable(
                name: "companies",
                schema: "org",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    legal_name = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    modified_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    modified_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_companies", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "dashboards",
                schema: "dashboard",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    description = table.Column<string>(type: "text", nullable: true),
                    is_system = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    modified_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    modified_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_dashboards", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "email_messages",
                schema: "ops",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    to = table.Column<string[]>(type: "text[]", nullable: false),
                    cc = table.Column<string[]>(type: "text[]", nullable: false),
                    subject = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    html_body = table.Column<string>(type: "text", nullable: true),
                    text_body = table.Column<string>(type: "text", nullable: true),
                    attachment_file_ids = table.Column<Guid[]>(type: "uuid[]", nullable: false),
                    template_code = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    status = table.Column<int>(type: "integer", nullable: false),
                    attempts = table.Column<int>(type: "integer", nullable: false),
                    last_error = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    scheduled_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    sent_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    provider_message_id = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    correlation_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_email_messages", x => x.id);
                    table.CheckConstraint("ck_email_messages_attempts", "attempts >= 0");
                });

            migrationBuilder.CreateTable(
                name: "files",
                schema: "files",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    storage_key = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    original_file_name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    content_type = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    size_bytes = table.Column<long>(type: "bigint", nullable: false),
                    sha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    purpose = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    classification = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    owner_entity_type = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    owner_entity_id = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    status = table.Column<int>(type: "integer", nullable: false),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    modified_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    modified_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_files", x => x.id);
                    table.CheckConstraint("ck_files_size", "size_bytes > 0");
                });

            migrationBuilder.CreateTable(
                name: "job_executions",
                schema: "ops",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    job_type = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    idempotency_key = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    status = table.Column<int>(type: "integer", nullable: false),
                    attempt = table.Column<int>(type: "integer", nullable: false),
                    started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    error = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    correlation_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_job_executions", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "permissions",
                schema: "identity",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    module_code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    kind = table.Column<int>(type: "integer", nullable: false),
                    action = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    description = table.Column<string>(type: "text", nullable: false),
                    supports_scope = table.Column<bool>(type: "boolean", nullable: false),
                    is_sensitive = table.Column<bool>(type: "boolean", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_permissions", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "roles",
                schema: "identity",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    description = table.Column<string>(type: "text", nullable: true),
                    is_system = table.Column<bool>(type: "boolean", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    modified_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    modified_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_roles", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "security_events",
                schema: "audit",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: true),
                    event_type = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    success = table.Column<bool>(type: "boolean", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    user_email = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    actor_id = table.Column<Guid>(type: "uuid", nullable: true),
                    ip_address = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    user_agent = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    correlation_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    details = table.Column<string>(type: "jsonb", nullable: true),
                    content_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_security_events", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "settings",
                schema: "config",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    key = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    value = table.Column<string>(type: "jsonb", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    modified_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    modified_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_settings", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "templates",
                schema: "notify",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    channel = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    locale = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    subject = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    body = table.Column<string>(type: "text", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    is_system = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    modified_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    modified_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_templates", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "tenant_identity",
                schema: "ops",
                columns: table => new
                {
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_number = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_tenant_identity", x => x.tenant_id);
                });

            migrationBuilder.CreateTable(
                name: "branches",
                schema: "org",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    time_zone = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    modified_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    modified_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_branches", x => x.id);
                    table.ForeignKey(
                        name: "fk_branches_companies_company_id",
                        column: x => x.company_id,
                        principalSchema: "org",
                        principalTable: "companies",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "dashboard_assignments",
                schema: "dashboard",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    dashboard_id = table.Column<Guid>(type: "uuid", nullable: false),
                    assignee_type = table.Column<int>(type: "integer", nullable: false),
                    assignee_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    modified_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    modified_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_dashboard_assignments", x => x.id);
                    table.ForeignKey(
                        name: "fk_dashboard_assignments_dashboards_dashboard_id",
                        column: x => x.dashboard_id,
                        principalSchema: "dashboard",
                        principalTable: "dashboards",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "dashboard_widgets",
                schema: "dashboard",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    dashboard_id = table.Column<Guid>(type: "uuid", nullable: false),
                    widget_code = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    x = table.Column<int>(type: "integer", nullable: false),
                    y = table.Column<int>(type: "integer", nullable: false),
                    width = table.Column<int>(type: "integer", nullable: false),
                    height = table.Column<int>(type: "integer", nullable: false),
                    settings = table.Column<string>(type: "jsonb", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_dashboard_widgets", x => x.id);
                    table.CheckConstraint("ck_dashboard_widgets_size", "width BETWEEN 1 AND 12 AND height BETWEEN 1 AND 12 AND x >= 0 AND y >= 0");
                    table.ForeignKey(
                        name: "fk_dashboard_widgets_dashboards_dashboard_id",
                        column: x => x.dashboard_id,
                        principalSchema: "dashboard",
                        principalTable: "dashboards",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "file_access_log",
                schema: "files",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    file_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    action = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ip_address = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_file_access_log", x => x.id);
                    table.ForeignKey(
                        name: "fk_file_access_log_files_file_id",
                        column: x => x.file_id,
                        principalSchema: "files",
                        principalTable: "files",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "role_permissions",
                schema: "identity",
                columns: table => new
                {
                    role_id = table.Column<Guid>(type: "uuid", nullable: false),
                    permission_id = table.Column<Guid>(type: "uuid", nullable: false),
                    scope_level = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_role_permissions", x => new { x.role_id, x.permission_id });
                    table.CheckConstraint("ck_role_permissions_scope", "scope_level BETWEEN 1 AND 8");
                    table.ForeignKey(
                        name: "fk_role_permissions_permissions_permission_id",
                        column: x => x.permission_id,
                        principalSchema: "identity",
                        principalTable: "permissions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_role_permissions_roles_role_id",
                        column: x => x.role_id,
                        principalSchema: "identity",
                        principalTable: "roles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "departments",
                schema: "org",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    branch_id = table.Column<Guid>(type: "uuid", nullable: true),
                    parent_department_id = table.Column<Guid>(type: "uuid", nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    modified_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    modified_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_departments", x => x.id);
                    table.CheckConstraint("ck_departments_parent", "parent_department_id IS NULL OR parent_department_id <> id");
                    table.ForeignKey(
                        name: "fk_departments_branches_branch_id",
                        column: x => x.branch_id,
                        principalSchema: "org",
                        principalTable: "branches",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_departments_departments_parent_department_id",
                        column: x => x.parent_department_id,
                        principalSchema: "org",
                        principalTable: "departments",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "employees",
                schema: "org",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    employee_code = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    display_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    work_email = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    branch_id = table.Column<Guid>(type: "uuid", nullable: false),
                    department_id = table.Column<Guid>(type: "uuid", nullable: true),
                    manager_employee_id = table.Column<Guid>(type: "uuid", nullable: true),
                    status = table.Column<int>(type: "integer", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    modified_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    modified_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_employees", x => x.id);
                    table.CheckConstraint("ck_employees_manager", "manager_employee_id IS NULL OR manager_employee_id <> id");
                    table.CheckConstraint("ck_employees_status", "status BETWEEN 0 AND 3");
                    table.ForeignKey(
                        name: "fk_employees_branches_branch_id",
                        column: x => x.branch_id,
                        principalSchema: "org",
                        principalTable: "branches",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_employees_companies_company_id",
                        column: x => x.company_id,
                        principalSchema: "org",
                        principalTable: "companies",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_employees_departments_department_id",
                        column: x => x.department_id,
                        principalSchema: "org",
                        principalTable: "departments",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_employees_employees_manager_employee_id",
                        column: x => x.manager_employee_id,
                        principalSchema: "org",
                        principalTable: "employees",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "reporting_closure",
                schema: "org",
                columns: table => new
                {
                    ancestor_id = table.Column<Guid>(type: "uuid", nullable: false),
                    descendant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    depth = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_reporting_closure", x => new { x.ancestor_id, x.descendant_id });
                    table.CheckConstraint("ck_reporting_closure_depth", "depth >= 0");
                    table.ForeignKey(
                        name: "fk_reporting_closure_employees_ancestor_id",
                        column: x => x.ancestor_id,
                        principalSchema: "org",
                        principalTable: "employees",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_reporting_closure_employees_descendant_id",
                        column: x => x.descendant_id,
                        principalSchema: "org",
                        principalTable: "employees",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "users",
                schema: "identity",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    employee_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    modified_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    modified_by = table.Column<Guid>(type: "uuid", nullable: true),
                    email = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    display_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    password_hash = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    security_stamp = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    email_confirmed = table.Column<bool>(type: "boolean", nullable: false),
                    access_failed_count = table.Column<int>(type: "integer", nullable: false),
                    lockout_end_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    mfa_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    mfa_secret_protected = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    last_login_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    password_changed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_users", x => x.id);
                    table.CheckConstraint("ck_users_status", "status BETWEEN 0 AND 4");
                    table.ForeignKey(
                        name: "fk_users_employees_employee_id",
                        column: x => x.employee_id,
                        principalSchema: "org",
                        principalTable: "employees",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "notifications",
                schema: "notify",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    type = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    title = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    body = table.Column<string>(type: "text", nullable: true),
                    link = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    priority = table.Column<int>(type: "integer", nullable: false),
                    is_read = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    read_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_notifications", x => x.id);
                    table.ForeignKey(
                        name: "fk_notifications_users_user_id",
                        column: x => x.user_id,
                        principalSchema: "identity",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "preferences",
                schema: "notify",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    notification_type = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    channel = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    enabled = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_preferences", x => x.id);
                    table.ForeignKey(
                        name: "fk_preferences_users_user_id",
                        column: x => x.user_id,
                        principalSchema: "identity",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "sessions",
                schema: "identity",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_seen_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    revoked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    revoked_reason = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    client = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ip_address = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    user_agent = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    auth_methods = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_sessions", x => x.id);
                    table.ForeignKey(
                        name: "fk_sessions_users_user_id",
                        column: x => x.user_id,
                        principalSchema: "identity",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "user_permission_overrides",
                schema: "identity",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    permission_id = table.Column<Guid>(type: "uuid", nullable: false),
                    effect = table.Column<int>(type: "integer", nullable: false),
                    scope_level = table.Column<int>(type: "integer", nullable: true),
                    target_kind = table.Column<int>(type: "integer", nullable: true),
                    target_ids = table.Column<Guid[]>(type: "uuid[]", nullable: false),
                    valid_from = table.Column<DateOnly>(type: "date", nullable: false),
                    valid_to = table.Column<DateOnly>(type: "date", nullable: true),
                    reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    modified_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    modified_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_user_permission_overrides", x => x.id);
                    table.CheckConstraint("ck_upo_period", "valid_to IS NULL OR valid_to >= valid_from");
                    table.CheckConstraint("ck_upo_reason", "length(reason) > 0");
                    table.ForeignKey(
                        name: "fk_user_permission_overrides_permissions_permission_id",
                        column: x => x.permission_id,
                        principalSchema: "identity",
                        principalTable: "permissions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_user_permission_overrides_users_user_id",
                        column: x => x.user_id,
                        principalSchema: "identity",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "user_role_assignments",
                schema: "identity",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    role_id = table.Column<Guid>(type: "uuid", nullable: false),
                    valid_from = table.Column<DateOnly>(type: "date", nullable: false),
                    valid_to = table.Column<DateOnly>(type: "date", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    modified_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    modified_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_user_role_assignments", x => x.id);
                    table.CheckConstraint("ck_ura_period", "valid_to IS NULL OR valid_to >= valid_from");
                    table.ForeignKey(
                        name: "fk_user_role_assignments_roles_role_id",
                        column: x => x.role_id,
                        principalSchema: "identity",
                        principalTable: "roles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_user_role_assignments_users_user_id",
                        column: x => x.user_id,
                        principalSchema: "identity",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "user_tokens",
                schema: "identity",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    purpose = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    token_hash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    used_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_user_tokens", x => x.id);
                    table.ForeignKey(
                        name: "fk_user_tokens_users_user_id",
                        column: x => x.user_id,
                        principalSchema: "identity",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "assignment_scope_targets",
                schema: "identity",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    assignment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<int>(type: "integer", nullable: false),
                    target_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_assignment_scope_targets", x => x.id);
                    table.CheckConstraint("ck_ast_kind", "kind BETWEEN 1 AND 4");
                    table.ForeignKey(
                        name: "fk_assignment_scope_targets_user_role_assignments_assignment_id",
                        column: x => x.assignment_id,
                        principalSchema: "identity",
                        principalTable: "user_role_assignments",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_assignment_scope_targets_assignment_id_kind_target_id",
                schema: "identity",
                table: "assignment_scope_targets",
                columns: new[] { "assignment_id", "kind", "target_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_audit_entries_actor_id",
                schema: "audit",
                table: "audit_entries",
                column: "actor_id");

            migrationBuilder.CreateIndex(
                name: "ix_audit_entries_correlation_id",
                schema: "audit",
                table: "audit_entries",
                column: "correlation_id");

            migrationBuilder.CreateIndex(
                name: "ix_audit_entries_entity_type_entity_id",
                schema: "audit",
                table: "audit_entries",
                columns: new[] { "entity_type", "entity_id" });

            migrationBuilder.CreateIndex(
                name: "ix_audit_entries_occurred_at",
                schema: "audit",
                table: "audit_entries",
                column: "occurred_at");

            migrationBuilder.CreateIndex(
                name: "ix_audit_seals_range_end",
                schema: "audit",
                table: "audit_seals",
                column: "range_end",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_branches_code",
                schema: "org",
                table: "branches",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_branches_company_id",
                schema: "org",
                table: "branches",
                column: "company_id");

            migrationBuilder.CreateIndex(
                name: "ix_companies_code",
                schema: "org",
                table: "companies",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_dashboard_assignments_assignee_type_assignee_id",
                schema: "dashboard",
                table: "dashboard_assignments",
                columns: new[] { "assignee_type", "assignee_id" });

            migrationBuilder.CreateIndex(
                name: "ix_dashboard_assignments_dashboard_id",
                schema: "dashboard",
                table: "dashboard_assignments",
                column: "dashboard_id");

            migrationBuilder.CreateIndex(
                name: "ix_dashboard_widgets_dashboard_id",
                schema: "dashboard",
                table: "dashboard_widgets",
                column: "dashboard_id");

            migrationBuilder.CreateIndex(
                name: "ix_dashboards_code",
                schema: "dashboard",
                table: "dashboards",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_departments_branch_id",
                schema: "org",
                table: "departments",
                column: "branch_id");

            migrationBuilder.CreateIndex(
                name: "ix_departments_code",
                schema: "org",
                table: "departments",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_departments_parent_department_id",
                schema: "org",
                table: "departments",
                column: "parent_department_id");

            migrationBuilder.CreateIndex(
                name: "ix_email_messages_status_scheduled_at",
                schema: "ops",
                table: "email_messages",
                columns: new[] { "status", "scheduled_at" });

            migrationBuilder.CreateIndex(
                name: "ix_employees_branch_id",
                schema: "org",
                table: "employees",
                column: "branch_id");

            migrationBuilder.CreateIndex(
                name: "ix_employees_company_id",
                schema: "org",
                table: "employees",
                column: "company_id");

            migrationBuilder.CreateIndex(
                name: "ix_employees_department_id",
                schema: "org",
                table: "employees",
                column: "department_id");

            migrationBuilder.CreateIndex(
                name: "ix_employees_employee_code",
                schema: "org",
                table: "employees",
                column: "employee_code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_employees_manager_employee_id",
                schema: "org",
                table: "employees",
                column: "manager_employee_id");

            migrationBuilder.CreateIndex(
                name: "ix_file_access_log_file_id_occurred_at",
                schema: "files",
                table: "file_access_log",
                columns: new[] { "file_id", "occurred_at" });

            migrationBuilder.CreateIndex(
                name: "ix_files_owner_entity_type_owner_entity_id",
                schema: "files",
                table: "files",
                columns: new[] { "owner_entity_type", "owner_entity_id" });

            migrationBuilder.CreateIndex(
                name: "ix_files_storage_key",
                schema: "files",
                table: "files",
                column: "storage_key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_job_executions_idempotency_key",
                schema: "ops",
                table: "job_executions",
                column: "idempotency_key",
                unique: true,
                filter: "idempotency_key IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_job_executions_job_type_started_at",
                schema: "ops",
                table: "job_executions",
                columns: new[] { "job_type", "started_at" });

            migrationBuilder.CreateIndex(
                name: "ix_notifications_user_id_is_read_created_at",
                schema: "notify",
                table: "notifications",
                columns: new[] { "user_id", "is_read", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_permissions_code",
                schema: "identity",
                table: "permissions",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_preferences_user_id_notification_type_channel",
                schema: "notify",
                table: "preferences",
                columns: new[] { "user_id", "notification_type", "channel" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_reporting_closure_descendant_id",
                schema: "org",
                table: "reporting_closure",
                column: "descendant_id");

            migrationBuilder.CreateIndex(
                name: "ix_role_permissions_permission_id",
                schema: "identity",
                table: "role_permissions",
                column: "permission_id");

            migrationBuilder.CreateIndex(
                name: "ix_roles_code",
                schema: "identity",
                table: "roles",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_security_events_event_type_occurred_at",
                schema: "audit",
                table: "security_events",
                columns: new[] { "event_type", "occurred_at" });

            migrationBuilder.CreateIndex(
                name: "ix_security_events_occurred_at",
                schema: "audit",
                table: "security_events",
                column: "occurred_at");

            migrationBuilder.CreateIndex(
                name: "ix_security_events_user_id",
                schema: "audit",
                table: "security_events",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_sessions_user_id_revoked_at",
                schema: "identity",
                table: "sessions",
                columns: new[] { "user_id", "revoked_at" });

            migrationBuilder.CreateIndex(
                name: "ix_settings_key",
                schema: "config",
                table: "settings",
                column: "key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_templates_code_channel_locale",
                schema: "notify",
                table: "templates",
                columns: new[] { "code", "channel", "locale" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_user_permission_overrides_permission_id",
                schema: "identity",
                table: "user_permission_overrides",
                column: "permission_id");

            migrationBuilder.CreateIndex(
                name: "ix_user_permission_overrides_user_id_permission_id",
                schema: "identity",
                table: "user_permission_overrides",
                columns: new[] { "user_id", "permission_id" });

            migrationBuilder.CreateIndex(
                name: "ix_user_role_assignments_role_id",
                schema: "identity",
                table: "user_role_assignments",
                column: "role_id");

            migrationBuilder.CreateIndex(
                name: "ix_user_role_assignments_user_id_role_id",
                schema: "identity",
                table: "user_role_assignments",
                columns: new[] { "user_id", "role_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_user_tokens_token_hash",
                schema: "identity",
                table: "user_tokens",
                column: "token_hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_user_tokens_user_id_purpose",
                schema: "identity",
                table: "user_tokens",
                columns: new[] { "user_id", "purpose" });

            migrationBuilder.CreateIndex(
                name: "ix_users_email",
                schema: "identity",
                table: "users",
                column: "email",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_users_employee_id",
                schema: "identity",
                table: "users",
                column: "employee_id",
                unique: true,
                filter: "employee_id IS NOT NULL");
            migrationBuilder.Sql(AuditTriggers.Create);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(AuditTriggers.Drop);
            migrationBuilder.DropTable(
                name: "assignment_scope_targets",
                schema: "identity");

            migrationBuilder.DropTable(
                name: "audit_entries",
                schema: "audit");

            migrationBuilder.DropTable(
                name: "audit_seals",
                schema: "audit");

            migrationBuilder.DropTable(
                name: "authorization_state",
                schema: "identity");

            migrationBuilder.DropTable(
                name: "dashboard_assignments",
                schema: "dashboard");

            migrationBuilder.DropTable(
                name: "dashboard_widgets",
                schema: "dashboard");

            migrationBuilder.DropTable(
                name: "email_messages",
                schema: "ops");

            migrationBuilder.DropTable(
                name: "file_access_log",
                schema: "files");

            migrationBuilder.DropTable(
                name: "job_executions",
                schema: "ops");

            migrationBuilder.DropTable(
                name: "notifications",
                schema: "notify");

            migrationBuilder.DropTable(
                name: "preferences",
                schema: "notify");

            migrationBuilder.DropTable(
                name: "reporting_closure",
                schema: "org");

            migrationBuilder.DropTable(
                name: "role_permissions",
                schema: "identity");

            migrationBuilder.DropTable(
                name: "security_events",
                schema: "audit");

            migrationBuilder.DropTable(
                name: "sessions",
                schema: "identity");

            migrationBuilder.DropTable(
                name: "settings",
                schema: "config");

            migrationBuilder.DropTable(
                name: "templates",
                schema: "notify");

            migrationBuilder.DropTable(
                name: "tenant_identity",
                schema: "ops");

            migrationBuilder.DropTable(
                name: "user_permission_overrides",
                schema: "identity");

            migrationBuilder.DropTable(
                name: "user_tokens",
                schema: "identity");

            migrationBuilder.DropTable(
                name: "user_role_assignments",
                schema: "identity");

            migrationBuilder.DropTable(
                name: "dashboards",
                schema: "dashboard");

            migrationBuilder.DropTable(
                name: "files",
                schema: "files");

            migrationBuilder.DropTable(
                name: "permissions",
                schema: "identity");

            migrationBuilder.DropTable(
                name: "roles",
                schema: "identity");

            migrationBuilder.DropTable(
                name: "users",
                schema: "identity");

            migrationBuilder.DropTable(
                name: "employees",
                schema: "org");

            migrationBuilder.DropTable(
                name: "departments",
                schema: "org");

            migrationBuilder.DropTable(
                name: "branches",
                schema: "org");

            migrationBuilder.DropTable(
                name: "companies",
                schema: "org");
        }
    }
}
