using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hrms.Infrastructure.Persistence.Migrations.Tenant
{
    /// <inheritdoc />
    public partial class AttendanceMasters : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "att");

            migrationBuilder.CreateTable(
                name: "attendance_policies",
                schema: "att",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    name = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    is_default = table.Column<bool>(type: "boolean", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    modified_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    modified_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_attendance_policies", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "shifts",
                schema: "att",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    modified_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    modified_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_shifts", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "work_calendars",
                schema: "att",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    name = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    year = table.Column<int>(type: "integer", nullable: true),
                    is_default = table.Column<bool>(type: "boolean", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    modified_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    modified_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_work_calendars", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "policy_assignments",
                schema: "att",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    policy_id = table.Column<Guid>(type: "uuid", nullable: false),
                    effective_from = table.Column<DateOnly>(type: "date", nullable: false),
                    effective_to = table.Column<DateOnly>(type: "date", nullable: true),
                    reason = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    modified_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    modified_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_policy_assignments", x => x.id);
                    table.ForeignKey(
                        name: "fk_policy_assignments_attendance_policies_policy_id",
                        column: x => x.policy_id,
                        principalSchema: "att",
                        principalTable: "attendance_policies",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_policy_assignments_employees_employee_id",
                        column: x => x.employee_id,
                        principalSchema: "org",
                        principalTable: "employees",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "policy_versions",
                schema: "att",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    policy_id = table.Column<Guid>(type: "uuid", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    effective_from = table.Column<DateOnly>(type: "date", nullable: false),
                    effective_to = table.Column<DateOnly>(type: "date", nullable: true),
                    rules_json = table.Column<string>(type: "jsonb", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    modified_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    modified_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_policy_versions", x => x.id);
                    table.CheckConstraint("ck_policy_versions_status", "status IN ('draft','published','retired')");
                    table.ForeignKey(
                        name: "fk_policy_versions_attendance_policies_policy_id",
                        column: x => x.policy_id,
                        principalSchema: "att",
                        principalTable: "attendance_policies",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "rosters",
                schema: "att",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    work_date = table.Column<DateOnly>(type: "date", nullable: false),
                    shift_id = table.Column<Guid>(type: "uuid", nullable: true),
                    is_off = table.Column<bool>(type: "boolean", nullable: false),
                    reason = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: true),
                    source = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    modified_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    modified_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_rosters", x => x.id);
                    table.CheckConstraint("ck_rosters_off_or_shift", "is_off OR shift_id IS NOT NULL");
                    table.ForeignKey(
                        name: "fk_rosters_employees_employee_id",
                        column: x => x.employee_id,
                        principalSchema: "org",
                        principalTable: "employees",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_rosters_shifts_shift_id",
                        column: x => x.shift_id,
                        principalSchema: "att",
                        principalTable: "shifts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "shift_assignments",
                schema: "att",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    shift_id = table.Column<Guid>(type: "uuid", nullable: false),
                    effective_from = table.Column<DateOnly>(type: "date", nullable: false),
                    effective_to = table.Column<DateOnly>(type: "date", nullable: true),
                    reason = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: true),
                    source = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    modified_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    modified_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_shift_assignments", x => x.id);
                    table.ForeignKey(
                        name: "fk_shift_assignments_employees_employee_id",
                        column: x => x.employee_id,
                        principalSchema: "org",
                        principalTable: "employees",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_shift_assignments_shifts_shift_id",
                        column: x => x.shift_id,
                        principalSchema: "att",
                        principalTable: "shifts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "shift_versions",
                schema: "att",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    shift_id = table.Column<Guid>(type: "uuid", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    effective_from = table.Column<DateOnly>(type: "date", nullable: false),
                    effective_to = table.Column<DateOnly>(type: "date", nullable: true),
                    shift_type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    start_time = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                    end_time = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                    break_minutes = table.Column<int>(type: "integer", nullable: false),
                    break_paid = table.Column<bool>(type: "boolean", nullable: false),
                    core_start = table.Column<TimeOnly>(type: "time without time zone", nullable: true),
                    core_end = table.Column<TimeOnly>(type: "time without time zone", nullable: true),
                    required_minutes = table.Column<int>(type: "integer", nullable: true),
                    grace_in_minutes = table.Column<int>(type: "integer", nullable: true),
                    grace_out_minutes = table.Column<int>(type: "integer", nullable: true),
                    in_window_before_minutes = table.Column<int>(type: "integer", nullable: false),
                    out_window_after_minutes = table.Column<int>(type: "integer", nullable: false),
                    ot_allowed = table.Column<bool>(type: "boolean", nullable: false),
                    weekly_off_config_json = table.Column<string>(type: "jsonb", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    modified_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    modified_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_shift_versions", x => x.id);
                    table.CheckConstraint("ck_shift_versions_status", "status IN ('draft','published','retired')");
                    table.ForeignKey(
                        name: "fk_shift_versions_shifts_shift_id",
                        column: x => x.shift_id,
                        principalSchema: "att",
                        principalTable: "shifts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "calendar_days",
                schema: "att",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    calendar_id = table.Column<Guid>(type: "uuid", nullable: false),
                    date = table.Column<DateOnly>(type: "date", nullable: false),
                    day_type = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    name = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    half_day_minutes = table.Column<int>(type: "integer", nullable: true),
                    applies_to_json = table.Column<string>(type: "jsonb", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    modified_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    modified_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_calendar_days", x => x.id);
                    table.CheckConstraint("ck_calendar_days_type", "day_type IN ('holiday','optional_holiday','working_override','half_day')");
                    table.ForeignKey(
                        name: "fk_calendar_days_work_calendars_calendar_id",
                        column: x => x.calendar_id,
                        principalSchema: "att",
                        principalTable: "work_calendars",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_attendance_policies_code",
                schema: "att",
                table: "attendance_policies",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_calendar_days_calendar_id_date",
                schema: "att",
                table: "calendar_days",
                columns: new[] { "calendar_id", "date" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_policy_assignments_employee_id_effective_from",
                schema: "att",
                table: "policy_assignments",
                columns: new[] { "employee_id", "effective_from" });

            migrationBuilder.CreateIndex(
                name: "ix_policy_assignments_policy_id",
                schema: "att",
                table: "policy_assignments",
                column: "policy_id");

            migrationBuilder.CreateIndex(
                name: "ix_policy_versions_policy_id_effective_from",
                schema: "att",
                table: "policy_versions",
                columns: new[] { "policy_id", "effective_from" });

            migrationBuilder.CreateIndex(
                name: "ix_policy_versions_policy_id_version",
                schema: "att",
                table: "policy_versions",
                columns: new[] { "policy_id", "version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_rosters_employee_id_work_date",
                schema: "att",
                table: "rosters",
                columns: new[] { "employee_id", "work_date" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_rosters_shift_id",
                schema: "att",
                table: "rosters",
                column: "shift_id");

            migrationBuilder.CreateIndex(
                name: "ix_shift_assignments_employee_id_effective_from",
                schema: "att",
                table: "shift_assignments",
                columns: new[] { "employee_id", "effective_from" });

            migrationBuilder.CreateIndex(
                name: "ix_shift_assignments_shift_id",
                schema: "att",
                table: "shift_assignments",
                column: "shift_id");

            migrationBuilder.CreateIndex(
                name: "ix_shift_versions_shift_id_effective_from",
                schema: "att",
                table: "shift_versions",
                columns: new[] { "shift_id", "effective_from" });

            migrationBuilder.CreateIndex(
                name: "ix_shift_versions_shift_id_version",
                schema: "att",
                table: "shift_versions",
                columns: new[] { "shift_id", "version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_shifts_code",
                schema: "att",
                table: "shifts",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_work_calendars_code",
                schema: "att",
                table: "work_calendars",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_work_calendars_is_default",
                schema: "att",
                table: "work_calendars",
                column: "is_default",
                filter: "is_default");

            // No-overlap guarantees on effective windows. btree_gist (created by HrCoreSchema)
            // supplies equality on scalar keys; daterange supplies overlap on the window.
            migrationBuilder.Sql("CREATE EXTENSION IF NOT EXISTS btree_gist;");

            // One published shift version per shift per day.
            migrationBuilder.Sql("""
                alter table att.shift_versions
                add constraint ex_shift_versions_no_overlap
                exclude using gist (
                  shift_id with =,
                  daterange(effective_from, coalesce(effective_to, 'infinity'::date), '[)') with &&
                ) where (status = 'published');
                """);

            // One published policy version per policy per day.
            migrationBuilder.Sql("""
                alter table att.policy_versions
                add constraint ex_policy_versions_no_overlap
                exclude using gist (
                  policy_id with =,
                  daterange(effective_from, coalesce(effective_to, 'infinity'::date), '[)') with &&
                ) where (status = 'published');
                """);

            // One active shift assignment per employee per day (draft or not — windows may not overlap at all).
            migrationBuilder.Sql("""
                alter table att.shift_assignments
                add constraint ex_shift_assignments_no_overlap
                exclude using gist (
                  employee_id with =,
                  daterange(effective_from, coalesce(effective_to, 'infinity'::date), '[)') with &&
                );
                """);

            // One active policy assignment per employee per day.
            migrationBuilder.Sql("""
                alter table att.policy_assignments
                add constraint ex_policy_assignments_no_overlap
                exclude using gist (
                  employee_id with =,
                  daterange(effective_from, coalesce(effective_to, 'infinity'::date), '[)') with &&
                );
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("alter table att.shift_versions drop constraint ex_shift_versions_no_overlap;");
            migrationBuilder.Sql("alter table att.policy_versions drop constraint ex_policy_versions_no_overlap;");
            migrationBuilder.Sql("alter table att.shift_assignments drop constraint ex_shift_assignments_no_overlap;");
            migrationBuilder.Sql("alter table att.policy_assignments drop constraint ex_policy_assignments_no_overlap;");

            migrationBuilder.DropTable(
                name: "calendar_days",
                schema: "att");

            migrationBuilder.DropTable(
                name: "policy_assignments",
                schema: "att");

            migrationBuilder.DropTable(
                name: "policy_versions",
                schema: "att");

            migrationBuilder.DropTable(
                name: "rosters",
                schema: "att");

            migrationBuilder.DropTable(
                name: "shift_assignments",
                schema: "att");

            migrationBuilder.DropTable(
                name: "shift_versions",
                schema: "att");

            migrationBuilder.DropTable(
                name: "work_calendars",
                schema: "att");

            migrationBuilder.DropTable(
                name: "attendance_policies",
                schema: "att");

            migrationBuilder.DropTable(
                name: "shifts",
                schema: "att");
        }
    }
}
