using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hrms.Infrastructure.Persistence.Migrations.Tenant
{
    /// <inheritdoc />
    public partial class HrCoreSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateOnly>(
                name: "confirmation_date",
                schema: "org",
                table: "employees",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "cost_centre_id",
                schema: "org",
                table: "employees",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "designation_id",
                schema: "org",
                table: "employees",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "employment_type",
                schema: "org",
                table: "employees",
                type: "character varying(24)",
                maxLength: 24,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "first_name",
                schema: "org",
                table: "employees",
                type: "character varying(80)",
                maxLength: 80,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "grade_id",
                schema: "org",
                table: "employees",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "joining_date",
                schema: "org",
                table: "employees",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "last_name",
                schema: "org",
                table: "employees",
                type: "character varying(80)",
                maxLength: 80,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "location_id",
                schema: "org",
                table: "employees",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "middle_name",
                schema: "org",
                table: "employees",
                type: "character varying(80)",
                maxLength: 80,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "photo_file_id",
                schema: "org",
                table: "employees",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "probation_end_date",
                schema: "org",
                table: "employees",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "user_id",
                schema: "org",
                table: "employees",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "work_phone",
                schema: "org",
                table: "employees",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "employee_code_series",
                schema: "org",
                columns: table => new
                {
                    series_key = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    next_value = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_employee_code_series", x => x.series_key);
                });

            migrationBuilder.CreateTable(
                name: "employee_numbering_config",
                schema: "org",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: true),
                    prefix = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    pad_width = table.Column<int>(type: "integer", nullable: false),
                    allow_manual = table.Column<bool>(type: "boolean", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    modified_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    modified_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_employee_numbering_config", x => x.id);
                    table.ForeignKey(
                        name: "fk_employee_numbering_config_companies_company_id",
                        column: x => x.company_id,
                        principalSchema: "org",
                        principalTable: "companies",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "lifecycle_events",
                schema: "org",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    event_type = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    payload_json = table.Column<string>(type: "jsonb", nullable: false),
                    effective_date = table.Column<DateOnly>(type: "date", nullable: false),
                    requested_by = table.Column<Guid>(type: "uuid", nullable: false),
                    requested_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    approved_by = table.Column<Guid>(type: "uuid", nullable: true),
                    approved_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    rejected_by = table.Column<Guid>(type: "uuid", nullable: true),
                    rejection_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    corrects_history_id = table.Column<Guid>(type: "uuid", nullable: true),
                    effective_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    modified_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    modified_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_lifecycle_events", x => x.id);
                    table.CheckConstraint("ck_lifecycle_events_status", "status BETWEEN 0 AND 5");
                    table.ForeignKey(
                        name: "fk_lifecycle_events_employees_employee_id",
                        column: x => x.employee_id,
                        principalSchema: "org",
                        principalTable: "employees",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_lifecycle_events_users_approved_by",
                        column: x => x.approved_by,
                        principalSchema: "identity",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_lifecycle_events_users_rejected_by",
                        column: x => x.rejected_by,
                        principalSchema: "identity",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_lifecycle_events_users_requested_by",
                        column: x => x.requested_by,
                        principalSchema: "identity",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "employment_history",
                schema: "org",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    branch_id = table.Column<Guid>(type: "uuid", nullable: false),
                    department_id = table.Column<Guid>(type: "uuid", nullable: true),
                    designation_id = table.Column<Guid>(type: "uuid", nullable: true),
                    grade_id = table.Column<Guid>(type: "uuid", nullable: true),
                    location_id = table.Column<Guid>(type: "uuid", nullable: true),
                    cost_centre_id = table.Column<Guid>(type: "uuid", nullable: true),
                    manager_employee_id = table.Column<Guid>(type: "uuid", nullable: true),
                    employment_type = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    event_type = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    effective_from = table.Column<DateOnly>(type: "date", nullable: false),
                    effective_to = table.Column<DateOnly>(type: "date", nullable: true),
                    reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    payroll_affecting = table.Column<bool>(type: "boolean", nullable: false),
                    lifecycle_event_id = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    modified_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    modified_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_employment_history", x => x.id);
                    table.ForeignKey(
                        name: "fk_employment_history_employees_employee_id",
                        column: x => x.employee_id,
                        principalSchema: "org",
                        principalTable: "employees",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_employment_history_lifecycle_events_lifecycle_event_id",
                        column: x => x.lifecycle_event_id,
                        principalSchema: "org",
                        principalTable: "lifecycle_events",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "ix_employees_cost_centre_id",
                schema: "org",
                table: "employees",
                column: "cost_centre_id");

            migrationBuilder.CreateIndex(
                name: "ix_employees_designation_id",
                schema: "org",
                table: "employees",
                column: "designation_id");

            migrationBuilder.CreateIndex(
                name: "ix_employees_grade_id",
                schema: "org",
                table: "employees",
                column: "grade_id");

            migrationBuilder.CreateIndex(
                name: "ix_employees_location_id",
                schema: "org",
                table: "employees",
                column: "location_id");

            migrationBuilder.CreateIndex(
                name: "ix_employees_photo_file_id",
                schema: "org",
                table: "employees",
                column: "photo_file_id");

            migrationBuilder.CreateIndex(
                name: "ix_employees_user_id",
                schema: "org",
                table: "employees",
                column: "user_id",
                unique: true,
                filter: "user_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_employee_numbering_config_company_id",
                schema: "org",
                table: "employee_numbering_config",
                column: "company_id",
                unique: true,
                filter: "company_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_employee_numbering_config_prefix",
                schema: "org",
                table: "employee_numbering_config",
                column: "prefix");

            migrationBuilder.CreateIndex(
                name: "ix_employment_history_employee_id",
                schema: "org",
                table: "employment_history",
                column: "employee_id");

            migrationBuilder.CreateIndex(
                name: "ix_employment_history_employee_id_effective_from",
                schema: "org",
                table: "employment_history",
                columns: new[] { "employee_id", "effective_from" });

            migrationBuilder.CreateIndex(
                name: "ix_employment_history_lifecycle_event_id",
                schema: "org",
                table: "employment_history",
                column: "lifecycle_event_id");

            migrationBuilder.CreateIndex(
                name: "ix_lifecycle_events_approved_by",
                schema: "org",
                table: "lifecycle_events",
                column: "approved_by");

            migrationBuilder.CreateIndex(
                name: "ix_lifecycle_events_employee_id",
                schema: "org",
                table: "lifecycle_events",
                column: "employee_id");

            migrationBuilder.CreateIndex(
                name: "ix_lifecycle_events_rejected_by",
                schema: "org",
                table: "lifecycle_events",
                column: "rejected_by");

            migrationBuilder.CreateIndex(
                name: "ix_lifecycle_events_requested_by",
                schema: "org",
                table: "lifecycle_events",
                column: "requested_by");

            migrationBuilder.CreateIndex(
                name: "ix_lifecycle_events_status_effective_date",
                schema: "org",
                table: "lifecycle_events",
                columns: new[] { "status", "effective_date" });

            migrationBuilder.AddForeignKey(
                name: "fk_employees_cost_centres_cost_centre_id",
                schema: "org",
                table: "employees",
                column: "cost_centre_id",
                principalSchema: "org",
                principalTable: "cost_centres",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_employees_designations_designation_id",
                schema: "org",
                table: "employees",
                column: "designation_id",
                principalSchema: "org",
                principalTable: "designations",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_employees_files_photo_file_id",
                schema: "org",
                table: "employees",
                column: "photo_file_id",
                principalSchema: "files",
                principalTable: "files",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "fk_employees_grades_grade_id",
                schema: "org",
                table: "employees",
                column: "grade_id",
                principalSchema: "org",
                principalTable: "grades",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_employees_users_user_id",
                schema: "org",
                table: "employees",
                column: "user_id",
                principalSchema: "identity",
                principalTable: "users",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_employees_work_locations_location_id",
                schema: "org",
                table: "employees",
                column: "location_id",
                principalSchema: "org",
                principalTable: "work_locations",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            // --- M2.5a: DB-level guards + idempotent bootstrap of existing employees (additive only) ---

            // No-overlap on employment periods: EXCLUDE needs btree_gist for the employee_id equality part.
            migrationBuilder.Sql("CREATE EXTENSION IF NOT EXISTS btree_gist;");
            migrationBuilder.Sql("""
                ALTER TABLE org.employment_history
                ADD CONSTRAINT ex_employment_history_no_overlap
                EXCLUDE USING gist (
                    employee_id WITH =,
                    daterange(effective_from, COALESCE(effective_to, 'infinity'::date), '[)') WITH &&
                );
                """);

            // Bootstrap one open history row per existing employee; effective from joining_date or record creation.
            migrationBuilder.Sql("""
                INSERT INTO org.employment_history
                    (id, employee_id, company_id, branch_id, department_id, designation_id, grade_id, location_id,
                     cost_centre_id, manager_employee_id, employment_type, event_type, effective_from, effective_to,
                     payroll_affecting, created_at)
                SELECT gen_random_uuid(), e.id, e.company_id, e.branch_id, e.department_id, e.designation_id, e.grade_id,
                       e.location_id, e.cost_centre_id, e.manager_employee_id,
                       COALESCE(e.employment_type, 'permanent'), 'initial_assignment',
                       COALESCE(e.joining_date, e.created_at::date), NULL, false, now()
                FROM org.employees e
                WHERE NOT EXISTS (SELECT 1 FROM org.employment_history h WHERE h.employee_id = e.id);
                """);

            // Tenant-default numbering config (idempotent).
            migrationBuilder.Sql("""
                INSERT INTO org.employee_numbering_config (id, company_id, prefix, pad_width, allow_manual, is_active, created_at)
                SELECT gen_random_uuid(), NULL, 'EMP', 5, true, true, now()
                WHERE NOT EXISTS (SELECT 1 FROM org.employee_numbering_config WHERE company_id IS NULL);
                """);

            // Seed the EMP series above the highest existing numeric suffix so generated codes never collide.
            migrationBuilder.Sql("""
                INSERT INTO org.employee_code_series (series_key, next_value)
                SELECT 'EMP', COALESCE(MAX(regexp_replace(employee_code, '.*?([0-9]+)$', '\1')::bigint), 0) + 1
                FROM org.employees
                WHERE employee_code ~ '[0-9]+$'
                ON CONFLICT (series_key) DO NOTHING;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_employees_cost_centres_cost_centre_id",
                schema: "org",
                table: "employees");

            migrationBuilder.DropForeignKey(
                name: "fk_employees_designations_designation_id",
                schema: "org",
                table: "employees");

            migrationBuilder.DropForeignKey(
                name: "fk_employees_files_photo_file_id",
                schema: "org",
                table: "employees");

            migrationBuilder.DropForeignKey(
                name: "fk_employees_grades_grade_id",
                schema: "org",
                table: "employees");

            migrationBuilder.DropForeignKey(
                name: "fk_employees_users_user_id",
                schema: "org",
                table: "employees");

            migrationBuilder.DropForeignKey(
                name: "fk_employees_work_locations_location_id",
                schema: "org",
                table: "employees");

            migrationBuilder.DropTable(
                name: "employee_code_series",
                schema: "org");

            migrationBuilder.DropTable(
                name: "employee_numbering_config",
                schema: "org");

            migrationBuilder.DropTable(
                name: "employment_history",
                schema: "org");

            migrationBuilder.DropTable(
                name: "lifecycle_events",
                schema: "org");

            migrationBuilder.DropIndex(
                name: "ix_employees_cost_centre_id",
                schema: "org",
                table: "employees");

            migrationBuilder.DropIndex(
                name: "ix_employees_designation_id",
                schema: "org",
                table: "employees");

            migrationBuilder.DropIndex(
                name: "ix_employees_grade_id",
                schema: "org",
                table: "employees");

            migrationBuilder.DropIndex(
                name: "ix_employees_location_id",
                schema: "org",
                table: "employees");

            migrationBuilder.DropIndex(
                name: "ix_employees_photo_file_id",
                schema: "org",
                table: "employees");

            migrationBuilder.DropIndex(
                name: "ix_employees_user_id",
                schema: "org",
                table: "employees");

            migrationBuilder.DropColumn(
                name: "confirmation_date",
                schema: "org",
                table: "employees");

            migrationBuilder.DropColumn(
                name: "cost_centre_id",
                schema: "org",
                table: "employees");

            migrationBuilder.DropColumn(
                name: "designation_id",
                schema: "org",
                table: "employees");

            migrationBuilder.DropColumn(
                name: "employment_type",
                schema: "org",
                table: "employees");

            migrationBuilder.DropColumn(
                name: "first_name",
                schema: "org",
                table: "employees");

            migrationBuilder.DropColumn(
                name: "grade_id",
                schema: "org",
                table: "employees");

            migrationBuilder.DropColumn(
                name: "joining_date",
                schema: "org",
                table: "employees");

            migrationBuilder.DropColumn(
                name: "last_name",
                schema: "org",
                table: "employees");

            migrationBuilder.DropColumn(
                name: "location_id",
                schema: "org",
                table: "employees");

            migrationBuilder.DropColumn(
                name: "middle_name",
                schema: "org",
                table: "employees");

            migrationBuilder.DropColumn(
                name: "photo_file_id",
                schema: "org",
                table: "employees");

            migrationBuilder.DropColumn(
                name: "probation_end_date",
                schema: "org",
                table: "employees");

            migrationBuilder.DropColumn(
                name: "user_id",
                schema: "org",
                table: "employees");

            migrationBuilder.DropColumn(
                name: "work_phone",
                schema: "org",
                table: "employees");
        }
    }
}
