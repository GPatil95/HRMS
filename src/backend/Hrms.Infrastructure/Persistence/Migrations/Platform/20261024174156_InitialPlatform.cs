using Hrms.Infrastructure.Persistence;
﻿using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hrms.Infrastructure.Persistence.Migrations.Platform
{
    /// <inheritdoc />
    public partial class InitialPlatform : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "audit");

            migrationBuilder.EnsureSchema(
                name: "platform");

            migrationBuilder.EnsureSchema(
                name: "ops");

            migrationBuilder.EnsureSchema(
                name: "catalog");

            migrationBuilder.EnsureSchema(
                name: "identity");

            migrationBuilder.CreateSequence<int>(
                name: "tenant_number_seq",
                schema: "platform",
                startValue: 1001L);

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
                name: "db_clusters",
                schema: "platform",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    host = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    port = table.Column<int>(type: "integer", nullable: false),
                    admin_username = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    admin_secret_ref = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    region = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    max_tenants = table.Column<int>(type: "integer", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    modified_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    modified_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_db_clusters", x => x.id);
                    table.CheckConstraint("ck_db_clusters_port", "port BETWEEN 1 AND 65535");
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
                name: "modules",
                schema: "catalog",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    kind = table.Column<int>(type: "integer", nullable: false),
                    parent_code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    sort_order = table.Column<int>(type: "integer", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    modified_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    modified_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_modules", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "plans",
                schema: "catalog",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    is_public = table.Column<bool>(type: "boolean", nullable: false),
                    employee_limit = table.Column<int>(type: "integer", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    modified_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    modified_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_plans", x => x.id);
                    table.CheckConstraint("ck_plans_employee_limit", "employee_limit IS NULL OR employee_limit > 0");
                });

            migrationBuilder.CreateTable(
                name: "platform_permissions",
                schema: "identity",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    description = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_platform_permissions", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "platform_roles",
                schema: "identity",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    is_system = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    modified_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    modified_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_platform_roles", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "platform_users",
                schema: "identity",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
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
                    table.PrimaryKey("pk_platform_users", x => x.id);
                    table.CheckConstraint("ck_platform_users_status", "status BETWEEN 0 AND 4");
                });

            migrationBuilder.CreateTable(
                name: "secrets",
                schema: "ops",
                columns: table => new
                {
                    @ref = table.Column<string>(name: "ref", type: "character varying(256)", maxLength: 256, nullable: false),
                    ciphertext = table.Column<byte[]>(type: "bytea", nullable: false),
                    nonce = table.Column<byte[]>(type: "bytea", nullable: false),
                    tag = table.Column<byte[]>(type: "bytea", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_secrets", x => x.@ref);
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
                schema: "platform",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    key = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    value = table.Column<string>(type: "jsonb", nullable: false),
                    description = table.Column<string>(type: "text", nullable: true),
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
                name: "tenants",
                schema: "platform",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_number = table.Column<int>(type: "integer", nullable: false),
                    code = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    region = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    time_zone = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    admin_email = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    entitlement_version = table.Column<long>(type: "bigint", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    modified_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    modified_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_tenants", x => x.id);
                    table.CheckConstraint("ck_tenants_code", "code ~ '^[a-z][a-z0-9-]{1,30}[a-z0-9]$'");
                    table.CheckConstraint("ck_tenants_number", "tenant_number >= 1000");
                    table.CheckConstraint("ck_tenants_status", "status BETWEEN 0 AND 4");
                });

            migrationBuilder.CreateTable(
                name: "features",
                schema: "catalog",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    module_id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    value_type = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    default_value = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    modified_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    modified_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_features", x => x.id);
                    table.ForeignKey(
                        name: "fk_features_modules_module_id",
                        column: x => x.module_id,
                        principalSchema: "catalog",
                        principalTable: "modules",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "module_dependencies",
                schema: "catalog",
                columns: table => new
                {
                    module_id = table.Column<Guid>(type: "uuid", nullable: false),
                    depends_on_module_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_module_dependencies", x => new { x.module_id, x.depends_on_module_id });
                    table.CheckConstraint("ck_module_dependencies_self", "module_id <> depends_on_module_id");
                    table.ForeignKey(
                        name: "fk_module_dependencies_modules_depends_on_module_id",
                        column: x => x.depends_on_module_id,
                        principalSchema: "catalog",
                        principalTable: "modules",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_module_dependencies_modules_module_id",
                        column: x => x.module_id,
                        principalSchema: "catalog",
                        principalTable: "modules",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "plan_modules",
                schema: "catalog",
                columns: table => new
                {
                    plan_id = table.Column<Guid>(type: "uuid", nullable: false),
                    module_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_plan_modules", x => new { x.plan_id, x.module_id });
                    table.ForeignKey(
                        name: "fk_plan_modules_modules_module_id",
                        column: x => x.module_id,
                        principalSchema: "catalog",
                        principalTable: "modules",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_plan_modules_plans_plan_id",
                        column: x => x.plan_id,
                        principalSchema: "catalog",
                        principalTable: "plans",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "platform_role_permissions",
                schema: "identity",
                columns: table => new
                {
                    role_id = table.Column<Guid>(type: "uuid", nullable: false),
                    permission_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_platform_role_permissions", x => new { x.role_id, x.permission_id });
                    table.ForeignKey(
                        name: "fk_platform_role_permissions_platform_permissions_permission_id",
                        column: x => x.permission_id,
                        principalSchema: "identity",
                        principalTable: "platform_permissions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_platform_role_permissions_platform_roles_role_id",
                        column: x => x.role_id,
                        principalSchema: "identity",
                        principalTable: "platform_roles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "platform_sessions",
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
                    table.PrimaryKey("pk_platform_sessions", x => x.id);
                    table.ForeignKey(
                        name: "fk_platform_sessions_platform_users_user_id",
                        column: x => x.user_id,
                        principalSchema: "identity",
                        principalTable: "platform_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "platform_user_roles",
                schema: "identity",
                columns: table => new
                {
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    role_id = table.Column<Guid>(type: "uuid", nullable: false),
                    assigned_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    assigned_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_platform_user_roles", x => new { x.user_id, x.role_id });
                    table.ForeignKey(
                        name: "fk_platform_user_roles_platform_roles_role_id",
                        column: x => x.role_id,
                        principalSchema: "identity",
                        principalTable: "platform_roles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_platform_user_roles_platform_users_user_id",
                        column: x => x.user_id,
                        principalSchema: "identity",
                        principalTable: "platform_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "platform_user_tokens",
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
                    table.PrimaryKey("pk_platform_user_tokens", x => x.id);
                    table.ForeignKey(
                        name: "fk_platform_user_tokens_platform_users_user_id",
                        column: x => x.user_id,
                        principalSchema: "identity",
                        principalTable: "platform_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "provisioning_operations",
                schema: "platform",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    operation = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    attempt = table.Column<int>(type: "integer", nullable: false),
                    current_step = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    error = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    correlation_id = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    modified_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    modified_by = table.Column<Guid>(type: "uuid", nullable: true),
                    steps = table.Column<string>(type: "jsonb", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_provisioning_operations", x => x.id);
                    table.CheckConstraint("ck_provisioning_status", "status BETWEEN 0 AND 3");
                    table.ForeignKey(
                        name: "fk_provisioning_operations_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalSchema: "platform",
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "support_access_grants",
                schema: "platform",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    platform_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    ticket_ref = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    access_level = table.Column<int>(type: "integer", nullable: false),
                    duration_minutes = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    decided_by = table.Column<Guid>(type: "uuid", nullable: true),
                    decided_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    starts_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    modified_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    modified_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_support_access_grants", x => x.id);
                    table.CheckConstraint("ck_support_grants_duration", "duration_minutes BETWEEN 5 AND 240");
                    table.CheckConstraint("ck_support_grants_reason", "length(reason) > 0");
                    table.ForeignKey(
                        name: "fk_support_access_grants_platform_users_platform_user_id",
                        column: x => x.platform_user_id,
                        principalSchema: "identity",
                        principalTable: "platform_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_support_access_grants_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalSchema: "platform",
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "tenant_databases",
                schema: "platform",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    cluster_id = table.Column<Guid>(type: "uuid", nullable: false),
                    database_name = table.Column<string>(type: "character varying(63)", maxLength: 63, nullable: false),
                    app_role_name = table.Column<string>(type: "character varying(63)", maxLength: 63, nullable: false),
                    app_secret_ref = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    migrator_role_name = table.Column<string>(type: "character varying(63)", maxLength: 63, nullable: false),
                    migrator_secret_ref = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    schema_version = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    last_migrated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_backup_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_backup_ref = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    provisioned_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    modified_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    modified_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_tenant_databases", x => x.id);
                    table.CheckConstraint("ck_tenant_databases_status", "status BETWEEN 0 AND 3");
                    table.ForeignKey(
                        name: "fk_tenant_databases_db_clusters_cluster_id",
                        column: x => x.cluster_id,
                        principalSchema: "platform",
                        principalTable: "db_clusters",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_tenant_databases_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalSchema: "platform",
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "tenant_domains",
                schema: "platform",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    host_name = table.Column<string>(type: "character varying(253)", maxLength: 253, nullable: false),
                    is_primary = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_tenant_domains", x => x.id);
                    table.ForeignKey(
                        name: "fk_tenant_domains_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalSchema: "platform",
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "tenant_module_entitlements",
                schema: "platform",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    module_id = table.Column<Guid>(type: "uuid", nullable: false),
                    source = table.Column<int>(type: "integer", nullable: false),
                    is_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    valid_from = table.Column<DateOnly>(type: "date", nullable: false),
                    valid_to = table.Column<DateOnly>(type: "date", nullable: true),
                    reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    modified_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    modified_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_tenant_module_entitlements", x => x.id);
                    table.CheckConstraint("ck_tme_period", "valid_to IS NULL OR valid_to >= valid_from");
                    table.ForeignKey(
                        name: "fk_tenant_module_entitlements_modules_module_id",
                        column: x => x.module_id,
                        principalSchema: "catalog",
                        principalTable: "modules",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_tenant_module_entitlements_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalSchema: "platform",
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "tenant_subscriptions",
                schema: "platform",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    plan_id = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    starts_on = table.Column<DateOnly>(type: "date", nullable: false),
                    ends_on = table.Column<DateOnly>(type: "date", nullable: true),
                    trial_ends_on = table.Column<DateOnly>(type: "date", nullable: true),
                    grace_ends_on = table.Column<DateOnly>(type: "date", nullable: true),
                    employee_limit = table.Column<int>(type: "integer", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    modified_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    modified_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_tenant_subscriptions", x => x.id);
                    table.CheckConstraint("ck_tenant_subscriptions_period", "ends_on IS NULL OR ends_on >= starts_on");
                    table.ForeignKey(
                        name: "fk_tenant_subscriptions_plans_plan_id",
                        column: x => x.plan_id,
                        principalSchema: "catalog",
                        principalTable: "plans",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_tenant_subscriptions_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalSchema: "platform",
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "plan_features",
                schema: "catalog",
                columns: table => new
                {
                    plan_id = table.Column<Guid>(type: "uuid", nullable: false),
                    feature_id = table.Column<Guid>(type: "uuid", nullable: false),
                    value = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_plan_features", x => new { x.plan_id, x.feature_id });
                    table.ForeignKey(
                        name: "fk_plan_features_features_feature_id",
                        column: x => x.feature_id,
                        principalSchema: "catalog",
                        principalTable: "features",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_plan_features_plans_plan_id",
                        column: x => x.plan_id,
                        principalSchema: "catalog",
                        principalTable: "plans",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "tenant_feature_entitlements",
                schema: "platform",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    feature_id = table.Column<Guid>(type: "uuid", nullable: false),
                    value = table.Column<string>(type: "text", nullable: true),
                    source = table.Column<int>(type: "integer", nullable: false),
                    is_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    valid_from = table.Column<DateOnly>(type: "date", nullable: false),
                    valid_to = table.Column<DateOnly>(type: "date", nullable: true),
                    reason = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    modified_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    modified_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_tenant_feature_entitlements", x => x.id);
                    table.CheckConstraint("ck_tfe_period", "valid_to IS NULL OR valid_to >= valid_from");
                    table.ForeignKey(
                        name: "fk_tenant_feature_entitlements_features_feature_id",
                        column: x => x.feature_id,
                        principalSchema: "catalog",
                        principalTable: "features",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_tenant_feature_entitlements_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalSchema: "platform",
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

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
                name: "ix_db_clusters_name",
                schema: "platform",
                table: "db_clusters",
                column: "name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_email_messages_status_scheduled_at",
                schema: "ops",
                table: "email_messages",
                columns: new[] { "status", "scheduled_at" });

            migrationBuilder.CreateIndex(
                name: "ix_features_code",
                schema: "catalog",
                table: "features",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_features_module_id",
                schema: "catalog",
                table: "features",
                column: "module_id");

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
                name: "ix_module_dependencies_depends_on_module_id",
                schema: "catalog",
                table: "module_dependencies",
                column: "depends_on_module_id");

            migrationBuilder.CreateIndex(
                name: "ix_modules_code",
                schema: "catalog",
                table: "modules",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_plan_features_feature_id",
                schema: "catalog",
                table: "plan_features",
                column: "feature_id");

            migrationBuilder.CreateIndex(
                name: "ix_plan_modules_module_id",
                schema: "catalog",
                table: "plan_modules",
                column: "module_id");

            migrationBuilder.CreateIndex(
                name: "ix_plans_code_version",
                schema: "catalog",
                table: "plans",
                columns: new[] { "code", "version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_platform_permissions_code",
                schema: "identity",
                table: "platform_permissions",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_platform_role_permissions_permission_id",
                schema: "identity",
                table: "platform_role_permissions",
                column: "permission_id");

            migrationBuilder.CreateIndex(
                name: "ix_platform_roles_code",
                schema: "identity",
                table: "platform_roles",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_platform_sessions_user_id_revoked_at",
                schema: "identity",
                table: "platform_sessions",
                columns: new[] { "user_id", "revoked_at" });

            migrationBuilder.CreateIndex(
                name: "ix_platform_user_roles_role_id",
                schema: "identity",
                table: "platform_user_roles",
                column: "role_id");

            migrationBuilder.CreateIndex(
                name: "ix_platform_user_tokens_token_hash",
                schema: "identity",
                table: "platform_user_tokens",
                column: "token_hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_platform_user_tokens_user_id_purpose",
                schema: "identity",
                table: "platform_user_tokens",
                columns: new[] { "user_id", "purpose" });

            migrationBuilder.CreateIndex(
                name: "ix_platform_users_email",
                schema: "identity",
                table: "platform_users",
                column: "email",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_provisioning_operations_tenant_id_created_at",
                schema: "platform",
                table: "provisioning_operations",
                columns: new[] { "tenant_id", "created_at" });

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
                name: "ix_settings_key",
                schema: "platform",
                table: "settings",
                column: "key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_support_access_grants_platform_user_id",
                schema: "platform",
                table: "support_access_grants",
                column: "platform_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_support_access_grants_tenant_id_status",
                schema: "platform",
                table: "support_access_grants",
                columns: new[] { "tenant_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_tenant_databases_cluster_id_database_name",
                schema: "platform",
                table: "tenant_databases",
                columns: new[] { "cluster_id", "database_name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_tenant_databases_tenant_id",
                schema: "platform",
                table: "tenant_databases",
                column: "tenant_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_tenant_domains_host_name",
                schema: "platform",
                table: "tenant_domains",
                column: "host_name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_tenant_domains_tenant_id",
                schema: "platform",
                table: "tenant_domains",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_tenant_feature_entitlements_feature_id",
                schema: "platform",
                table: "tenant_feature_entitlements",
                column: "feature_id");

            migrationBuilder.CreateIndex(
                name: "ix_tenant_feature_entitlements_tenant_id_feature_id",
                schema: "platform",
                table: "tenant_feature_entitlements",
                columns: new[] { "tenant_id", "feature_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_tenant_module_entitlements_module_id",
                schema: "platform",
                table: "tenant_module_entitlements",
                column: "module_id");

            migrationBuilder.CreateIndex(
                name: "ix_tenant_module_entitlements_tenant_id_module_id",
                schema: "platform",
                table: "tenant_module_entitlements",
                columns: new[] { "tenant_id", "module_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_tenant_subscriptions_plan_id",
                schema: "platform",
                table: "tenant_subscriptions",
                column: "plan_id");

            migrationBuilder.CreateIndex(
                name: "ix_tenant_subscriptions_tenant_id_starts_on",
                schema: "platform",
                table: "tenant_subscriptions",
                columns: new[] { "tenant_id", "starts_on" });

            migrationBuilder.CreateIndex(
                name: "ix_tenants_code",
                schema: "platform",
                table: "tenants",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_tenants_tenant_number",
                schema: "platform",
                table: "tenants",
                column: "tenant_number",
                unique: true);
            migrationBuilder.Sql(AuditTriggers.Create);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(AuditTriggers.Drop);
            migrationBuilder.DropTable(
                name: "audit_entries",
                schema: "audit");

            migrationBuilder.DropTable(
                name: "audit_seals",
                schema: "audit");

            migrationBuilder.DropTable(
                name: "email_messages",
                schema: "ops");

            migrationBuilder.DropTable(
                name: "job_executions",
                schema: "ops");

            migrationBuilder.DropTable(
                name: "module_dependencies",
                schema: "catalog");

            migrationBuilder.DropTable(
                name: "plan_features",
                schema: "catalog");

            migrationBuilder.DropTable(
                name: "plan_modules",
                schema: "catalog");

            migrationBuilder.DropTable(
                name: "platform_role_permissions",
                schema: "identity");

            migrationBuilder.DropTable(
                name: "platform_sessions",
                schema: "identity");

            migrationBuilder.DropTable(
                name: "platform_user_roles",
                schema: "identity");

            migrationBuilder.DropTable(
                name: "platform_user_tokens",
                schema: "identity");

            migrationBuilder.DropTable(
                name: "provisioning_operations",
                schema: "platform");

            migrationBuilder.DropTable(
                name: "secrets",
                schema: "ops");

            migrationBuilder.DropTable(
                name: "security_events",
                schema: "audit");

            migrationBuilder.DropTable(
                name: "settings",
                schema: "platform");

            migrationBuilder.DropTable(
                name: "support_access_grants",
                schema: "platform");

            migrationBuilder.DropTable(
                name: "tenant_databases",
                schema: "platform");

            migrationBuilder.DropTable(
                name: "tenant_domains",
                schema: "platform");

            migrationBuilder.DropTable(
                name: "tenant_feature_entitlements",
                schema: "platform");

            migrationBuilder.DropTable(
                name: "tenant_module_entitlements",
                schema: "platform");

            migrationBuilder.DropTable(
                name: "tenant_subscriptions",
                schema: "platform");

            migrationBuilder.DropTable(
                name: "platform_permissions",
                schema: "identity");

            migrationBuilder.DropTable(
                name: "platform_roles",
                schema: "identity");

            migrationBuilder.DropTable(
                name: "platform_users",
                schema: "identity");

            migrationBuilder.DropTable(
                name: "db_clusters",
                schema: "platform");

            migrationBuilder.DropTable(
                name: "features",
                schema: "catalog");

            migrationBuilder.DropTable(
                name: "plans",
                schema: "catalog");

            migrationBuilder.DropTable(
                name: "tenants",
                schema: "platform");

            migrationBuilder.DropTable(
                name: "modules",
                schema: "catalog");

            migrationBuilder.DropSequence(
                name: "tenant_number_seq",
                schema: "platform");
        }
    }
}
