using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hrms.Infrastructure.Persistence.Migrations.Tenant
{
    /// <inheritdoc />
    public partial class OrgMasters : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "alt_hod_employee_id",
                schema: "org",
                table: "departments",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "alt_leave_approver_employee_id",
                schema: "org",
                table: "departments",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "approved_headcount",
                schema: "org",
                table: "departments",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "attendance_policy_code",
                schema: "org",
                table: "departments",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "break_minutes",
                schema: "org",
                table: "departments",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "budget_amount",
                schema: "org",
                table: "departments",
                type: "numeric(18,2)",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "comp_off_approver_employee_id",
                schema: "org",
                table: "departments",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "cost_centre",
                schema: "org",
                table: "departments",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "default_shift_name",
                schema: "org",
                table: "departments",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "department_type",
                schema: "org",
                table: "departments",
                type: "character varying(60)",
                maxLength: 60,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "description",
                schema: "org",
                table: "departments",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "effective_date",
                schema: "org",
                table: "departments",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "escalation_level",
                schema: "org",
                table: "departments",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "grace_minutes",
                schema: "org",
                table: "departments",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "hod_effective_from",
                schema: "org",
                table: "departments",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "hod_employee_id",
                schema: "org",
                table: "departments",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "holiday_calendar_code",
                schema: "org",
                table: "departments",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "leave_approver_employee_id",
                schema: "org",
                table: "departments",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "od_approver_employee_id",
                schema: "org",
                table: "departments",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ot_approver_employee_id",
                schema: "org",
                table: "departments",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "overtime_allowed",
                schema: "org",
                table: "departments",
                type: "boolean",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "regularization_approver_employee_id",
                schema: "org",
                table: "departments",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "reporting_manager_employee_id",
                schema: "org",
                table: "departments",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "weekly_off_config_json",
                schema: "org",
                table: "departments",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "weekly_off_pattern",
                schema: "org",
                table: "departments",
                type: "character varying(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "wfh_allowed",
                schema: "org",
                table: "departments",
                type: "boolean",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "work_area",
                schema: "org",
                table: "departments",
                type: "character varying(120)",
                maxLength: 120,
                nullable: true);

            migrationBuilder.AddColumn<TimeOnly>(
                name: "work_end",
                schema: "org",
                table: "departments",
                type: "time without time zone",
                nullable: true);

            migrationBuilder.AddColumn<TimeOnly>(
                name: "work_start",
                schema: "org",
                table: "departments",
                type: "time without time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "cin",
                schema: "org",
                table: "companies",
                type: "character varying(25)",
                maxLength: 25,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "company_type",
                schema: "org",
                table: "companies",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "contact_number",
                schema: "org",
                table: "companies",
                type: "character varying(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "currency",
                schema: "org",
                table: "companies",
                type: "character varying(8)",
                maxLength: 8,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "default_attendance_policy_code",
                schema: "org",
                table: "companies",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "default_holiday_calendar_code",
                schema: "org",
                table: "companies",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "default_leave_policy_code",
                schema: "org",
                table: "companies",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "default_shift_name",
                schema: "org",
                table: "companies",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "esic_employer_code",
                schema: "org",
                table: "companies",
                type: "character varying(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "factory_licence_no",
                schema: "org",
                table: "companies",
                type: "character varying(60)",
                maxLength: 60,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "financial_year_start_month",
                schema: "org",
                table: "companies",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "grace_minutes",
                schema: "org",
                table: "companies",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "incorporation_date",
                schema: "org",
                table: "companies",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "industry",
                schema: "org",
                table: "companies",
                type: "character varying(150)",
                maxLength: 150,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "logo_file_id",
                schema: "org",
                table: "companies",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "msme_udyam_reg_no",
                schema: "org",
                table: "companies",
                type: "character varying(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "official_email",
                schema: "org",
                table: "companies",
                type: "character varying(320)",
                maxLength: 320,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "pan",
                schema: "org",
                table: "companies",
                type: "character varying(15)",
                maxLength: 15,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "parent_company_id",
                schema: "org",
                table: "companies",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "payroll_frequency",
                schema: "org",
                table: "companies",
                type: "character varying(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "pf_establishment_code",
                schema: "org",
                table: "companies",
                type: "character varying(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "professional_tax_reg_no",
                schema: "org",
                table: "companies",
                type: "character varying(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "salary_pay_day",
                schema: "org",
                table: "companies",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "shops_establishment_reg_no",
                schema: "org",
                table: "companies",
                type: "character varying(60)",
                maxLength: 60,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "tan",
                schema: "org",
                table: "companies",
                type: "character varying(15)",
                maxLength: 15,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "website",
                schema: "org",
                table: "companies",
                type: "character varying(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "weekly_off_config_json",
                schema: "org",
                table: "companies",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "weekly_off_pattern",
                schema: "org",
                table: "companies",
                type: "character varying(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "address_line1",
                schema: "org",
                table: "branches",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "address_line2",
                schema: "org",
                table: "branches",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "attendance_mode",
                schema: "org",
                table: "branches",
                type: "character varying(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "attendance_policy_code",
                schema: "org",
                table: "branches",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "branch_type",
                schema: "org",
                table: "branches",
                type: "character varying(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "break_minutes",
                schema: "org",
                table: "branches",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "city",
                schema: "org",
                table: "branches",
                type: "character varying(80)",
                maxLength: 80,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "client_site_ref",
                schema: "org",
                table: "branches",
                type: "character varying(120)",
                maxLength: 120,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "contact_email",
                schema: "org",
                table: "branches",
                type: "character varying(320)",
                maxLength: 320,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "contact_phone",
                schema: "org",
                table: "branches",
                type: "character varying(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "cost_centre",
                schema: "org",
                table: "branches",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "country",
                schema: "org",
                table: "branches",
                type: "character varying(80)",
                maxLength: 80,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "default_shift_name",
                schema: "org",
                table: "branches",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "district",
                schema: "org",
                table: "branches",
                type: "character varying(80)",
                maxLength: 80,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "geofence_radius_meters",
                schema: "org",
                table: "branches",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "grace_minutes",
                schema: "org",
                table: "branches",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "holiday_calendar_code",
                schema: "org",
                table: "branches",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "hr_employee_id",
                schema: "org",
                table: "branches",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "latitude",
                schema: "org",
                table: "branches",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "longitude",
                schema: "org",
                table: "branches",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "manager_employee_id",
                schema: "org",
                table: "branches",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "opening_date",
                schema: "org",
                table: "branches",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "overtime_allowed",
                schema: "org",
                table: "branches",
                type: "boolean",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "payroll_day",
                schema: "org",
                table: "branches",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "pin_code",
                schema: "org",
                table: "branches",
                type: "character varying(12)",
                maxLength: 12,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "state",
                schema: "org",
                table: "branches",
                type: "character varying(80)",
                maxLength: 80,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "weekly_off_config_json",
                schema: "org",
                table: "branches",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "weekly_off_pattern",
                schema: "org",
                table: "branches",
                type: "character varying(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddColumn<TimeOnly>(
                name: "work_end",
                schema: "org",
                table: "branches",
                type: "time without time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "work_location_type",
                schema: "org",
                table: "branches",
                type: "character varying(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<TimeOnly>(
                name: "work_start",
                schema: "org",
                table: "branches",
                type: "time without time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "company_addresses",
                schema: "org",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    address_type = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    line1 = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    line2 = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    landmark = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    country = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    state = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    district = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    city = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    pin_code = table.Column<string>(type: "character varying(12)", maxLength: 12, nullable: true),
                    contact_person = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    contact_email = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: true),
                    contact_phone = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    modified_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    modified_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_company_addresses", x => x.id);
                    table.ForeignKey(
                        name: "fk_company_addresses_companies_company_id",
                        column: x => x.company_id,
                        principalSchema: "org",
                        principalTable: "companies",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "company_bank_accounts",
                schema: "org",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    bank_name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    branch_name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    account_number = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    ifsc = table.Column<string>(type: "character varying(15)", maxLength: 15, nullable: true),
                    is_primary = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    modified_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    modified_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_company_bank_accounts", x => x.id);
                    table.ForeignKey(
                        name: "fk_company_bank_accounts_companies_company_id",
                        column: x => x.company_id,
                        principalSchema: "org",
                        principalTable: "companies",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "company_contacts",
                schema: "org",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    contact_type = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    designation = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    email = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: true),
                    phone = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    modified_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    modified_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_company_contacts", x => x.id);
                    table.ForeignKey(
                        name: "fk_company_contacts_companies_company_id",
                        column: x => x.company_id,
                        principalSchema: "org",
                        principalTable: "companies",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "department_hod_history",
                schema: "org",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    department_id = table.Column<Guid>(type: "uuid", nullable: false),
                    role = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    employee_id = table.Column<Guid>(type: "uuid", nullable: true),
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
                    table.PrimaryKey("pk_department_hod_history", x => x.id);
                    table.ForeignKey(
                        name: "fk_department_hod_history_departments_department_id",
                        column: x => x.department_id,
                        principalSchema: "org",
                        principalTable: "departments",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_department_hod_history_employees_employee_id",
                        column: x => x.employee_id,
                        principalSchema: "org",
                        principalTable: "employees",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "org_registrations",
                schema: "org",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: true),
                    branch_id = table.Column<Guid>(type: "uuid", nullable: true),
                    registration_type = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    registration_number = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    jurisdiction = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    registration_subtype = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    effective_from = table.Column<DateOnly>(type: "date", nullable: true),
                    effective_to = table.Column<DateOnly>(type: "date", nullable: true),
                    document_file_id = table.Column<Guid>(type: "uuid", nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    modified_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    modified_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_org_registrations", x => x.id);
                    table.CheckConstraint("ck_org_registrations_owner", "(company_id IS NULL) <> (branch_id IS NULL)");
                    table.ForeignKey(
                        name: "fk_org_registrations_branches_branch_id",
                        column: x => x.branch_id,
                        principalSchema: "org",
                        principalTable: "branches",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_org_registrations_companies_company_id",
                        column: x => x.company_id,
                        principalSchema: "org",
                        principalTable: "companies",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_org_registrations_files_document_file_id",
                        column: x => x.document_file_id,
                        principalSchema: "files",
                        principalTable: "files",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "ix_departments_alt_hod_employee_id",
                schema: "org",
                table: "departments",
                column: "alt_hod_employee_id");

            migrationBuilder.CreateIndex(
                name: "ix_departments_alt_leave_approver_employee_id",
                schema: "org",
                table: "departments",
                column: "alt_leave_approver_employee_id");

            migrationBuilder.CreateIndex(
                name: "ix_departments_comp_off_approver_employee_id",
                schema: "org",
                table: "departments",
                column: "comp_off_approver_employee_id");

            migrationBuilder.CreateIndex(
                name: "ix_departments_hod_employee_id",
                schema: "org",
                table: "departments",
                column: "hod_employee_id");

            migrationBuilder.CreateIndex(
                name: "ix_departments_leave_approver_employee_id",
                schema: "org",
                table: "departments",
                column: "leave_approver_employee_id");

            migrationBuilder.CreateIndex(
                name: "ix_departments_od_approver_employee_id",
                schema: "org",
                table: "departments",
                column: "od_approver_employee_id");

            migrationBuilder.CreateIndex(
                name: "ix_departments_ot_approver_employee_id",
                schema: "org",
                table: "departments",
                column: "ot_approver_employee_id");

            migrationBuilder.CreateIndex(
                name: "ix_departments_regularization_approver_employee_id",
                schema: "org",
                table: "departments",
                column: "regularization_approver_employee_id");

            migrationBuilder.CreateIndex(
                name: "ix_departments_reporting_manager_employee_id",
                schema: "org",
                table: "departments",
                column: "reporting_manager_employee_id");

            migrationBuilder.CreateIndex(
                name: "ix_companies_logo_file_id",
                schema: "org",
                table: "companies",
                column: "logo_file_id");

            migrationBuilder.CreateIndex(
                name: "ix_companies_parent_company_id",
                schema: "org",
                table: "companies",
                column: "parent_company_id");

            migrationBuilder.CreateIndex(
                name: "ix_branches_hr_employee_id",
                schema: "org",
                table: "branches",
                column: "hr_employee_id");

            migrationBuilder.CreateIndex(
                name: "ix_branches_manager_employee_id",
                schema: "org",
                table: "branches",
                column: "manager_employee_id");

            migrationBuilder.CreateIndex(
                name: "ix_company_addresses_company_id",
                schema: "org",
                table: "company_addresses",
                column: "company_id");

            migrationBuilder.CreateIndex(
                name: "ix_company_bank_accounts_company_id",
                schema: "org",
                table: "company_bank_accounts",
                column: "company_id");

            migrationBuilder.CreateIndex(
                name: "ix_company_contacts_company_id",
                schema: "org",
                table: "company_contacts",
                column: "company_id");

            migrationBuilder.CreateIndex(
                name: "ix_department_hod_history_department_id",
                schema: "org",
                table: "department_hod_history",
                column: "department_id");

            migrationBuilder.CreateIndex(
                name: "ix_department_hod_history_employee_id",
                schema: "org",
                table: "department_hod_history",
                column: "employee_id");

            migrationBuilder.CreateIndex(
                name: "ix_org_registrations_branch_id",
                schema: "org",
                table: "org_registrations",
                column: "branch_id");

            migrationBuilder.CreateIndex(
                name: "ix_org_registrations_company_id",
                schema: "org",
                table: "org_registrations",
                column: "company_id");

            migrationBuilder.CreateIndex(
                name: "ix_org_registrations_company_id_registration_type_registration",
                schema: "org",
                table: "org_registrations",
                columns: new[] { "company_id", "registration_type", "registration_number" },
                unique: true,
                filter: "company_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_org_registrations_document_file_id",
                schema: "org",
                table: "org_registrations",
                column: "document_file_id");

            migrationBuilder.AddForeignKey(
                name: "fk_branches_employees_hr_employee_id",
                schema: "org",
                table: "branches",
                column: "hr_employee_id",
                principalSchema: "org",
                principalTable: "employees",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_branches_employees_manager_employee_id",
                schema: "org",
                table: "branches",
                column: "manager_employee_id",
                principalSchema: "org",
                principalTable: "employees",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_companies_companies_parent_company_id",
                schema: "org",
                table: "companies",
                column: "parent_company_id",
                principalSchema: "org",
                principalTable: "companies",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_companies_files_logo_file_id",
                schema: "org",
                table: "companies",
                column: "logo_file_id",
                principalSchema: "files",
                principalTable: "files",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "fk_departments_employees_alt_hod_employee_id",
                schema: "org",
                table: "departments",
                column: "alt_hod_employee_id",
                principalSchema: "org",
                principalTable: "employees",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_departments_employees_alt_leave_approver_employee_id",
                schema: "org",
                table: "departments",
                column: "alt_leave_approver_employee_id",
                principalSchema: "org",
                principalTable: "employees",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_departments_employees_comp_off_approver_employee_id",
                schema: "org",
                table: "departments",
                column: "comp_off_approver_employee_id",
                principalSchema: "org",
                principalTable: "employees",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_departments_employees_hod_employee_id",
                schema: "org",
                table: "departments",
                column: "hod_employee_id",
                principalSchema: "org",
                principalTable: "employees",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_departments_employees_leave_approver_employee_id",
                schema: "org",
                table: "departments",
                column: "leave_approver_employee_id",
                principalSchema: "org",
                principalTable: "employees",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_departments_employees_od_approver_employee_id",
                schema: "org",
                table: "departments",
                column: "od_approver_employee_id",
                principalSchema: "org",
                principalTable: "employees",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_departments_employees_ot_approver_employee_id",
                schema: "org",
                table: "departments",
                column: "ot_approver_employee_id",
                principalSchema: "org",
                principalTable: "employees",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_departments_employees_regularization_approver_employee_id",
                schema: "org",
                table: "departments",
                column: "regularization_approver_employee_id",
                principalSchema: "org",
                principalTable: "employees",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_departments_employees_reporting_manager_employee_id",
                schema: "org",
                table: "departments",
                column: "reporting_manager_employee_id",
                principalSchema: "org",
                principalTable: "employees",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_branches_employees_hr_employee_id",
                schema: "org",
                table: "branches");

            migrationBuilder.DropForeignKey(
                name: "fk_branches_employees_manager_employee_id",
                schema: "org",
                table: "branches");

            migrationBuilder.DropForeignKey(
                name: "fk_companies_companies_parent_company_id",
                schema: "org",
                table: "companies");

            migrationBuilder.DropForeignKey(
                name: "fk_companies_files_logo_file_id",
                schema: "org",
                table: "companies");

            migrationBuilder.DropForeignKey(
                name: "fk_departments_employees_alt_hod_employee_id",
                schema: "org",
                table: "departments");

            migrationBuilder.DropForeignKey(
                name: "fk_departments_employees_alt_leave_approver_employee_id",
                schema: "org",
                table: "departments");

            migrationBuilder.DropForeignKey(
                name: "fk_departments_employees_comp_off_approver_employee_id",
                schema: "org",
                table: "departments");

            migrationBuilder.DropForeignKey(
                name: "fk_departments_employees_hod_employee_id",
                schema: "org",
                table: "departments");

            migrationBuilder.DropForeignKey(
                name: "fk_departments_employees_leave_approver_employee_id",
                schema: "org",
                table: "departments");

            migrationBuilder.DropForeignKey(
                name: "fk_departments_employees_od_approver_employee_id",
                schema: "org",
                table: "departments");

            migrationBuilder.DropForeignKey(
                name: "fk_departments_employees_ot_approver_employee_id",
                schema: "org",
                table: "departments");

            migrationBuilder.DropForeignKey(
                name: "fk_departments_employees_regularization_approver_employee_id",
                schema: "org",
                table: "departments");

            migrationBuilder.DropForeignKey(
                name: "fk_departments_employees_reporting_manager_employee_id",
                schema: "org",
                table: "departments");

            migrationBuilder.DropTable(
                name: "company_addresses",
                schema: "org");

            migrationBuilder.DropTable(
                name: "company_bank_accounts",
                schema: "org");

            migrationBuilder.DropTable(
                name: "company_contacts",
                schema: "org");

            migrationBuilder.DropTable(
                name: "department_hod_history",
                schema: "org");

            migrationBuilder.DropTable(
                name: "org_registrations",
                schema: "org");

            migrationBuilder.DropIndex(
                name: "ix_departments_alt_hod_employee_id",
                schema: "org",
                table: "departments");

            migrationBuilder.DropIndex(
                name: "ix_departments_alt_leave_approver_employee_id",
                schema: "org",
                table: "departments");

            migrationBuilder.DropIndex(
                name: "ix_departments_comp_off_approver_employee_id",
                schema: "org",
                table: "departments");

            migrationBuilder.DropIndex(
                name: "ix_departments_hod_employee_id",
                schema: "org",
                table: "departments");

            migrationBuilder.DropIndex(
                name: "ix_departments_leave_approver_employee_id",
                schema: "org",
                table: "departments");

            migrationBuilder.DropIndex(
                name: "ix_departments_od_approver_employee_id",
                schema: "org",
                table: "departments");

            migrationBuilder.DropIndex(
                name: "ix_departments_ot_approver_employee_id",
                schema: "org",
                table: "departments");

            migrationBuilder.DropIndex(
                name: "ix_departments_regularization_approver_employee_id",
                schema: "org",
                table: "departments");

            migrationBuilder.DropIndex(
                name: "ix_departments_reporting_manager_employee_id",
                schema: "org",
                table: "departments");

            migrationBuilder.DropIndex(
                name: "ix_companies_logo_file_id",
                schema: "org",
                table: "companies");

            migrationBuilder.DropIndex(
                name: "ix_companies_parent_company_id",
                schema: "org",
                table: "companies");

            migrationBuilder.DropIndex(
                name: "ix_branches_hr_employee_id",
                schema: "org",
                table: "branches");

            migrationBuilder.DropIndex(
                name: "ix_branches_manager_employee_id",
                schema: "org",
                table: "branches");

            migrationBuilder.DropColumn(
                name: "alt_hod_employee_id",
                schema: "org",
                table: "departments");

            migrationBuilder.DropColumn(
                name: "alt_leave_approver_employee_id",
                schema: "org",
                table: "departments");

            migrationBuilder.DropColumn(
                name: "approved_headcount",
                schema: "org",
                table: "departments");

            migrationBuilder.DropColumn(
                name: "attendance_policy_code",
                schema: "org",
                table: "departments");

            migrationBuilder.DropColumn(
                name: "break_minutes",
                schema: "org",
                table: "departments");

            migrationBuilder.DropColumn(
                name: "budget_amount",
                schema: "org",
                table: "departments");

            migrationBuilder.DropColumn(
                name: "comp_off_approver_employee_id",
                schema: "org",
                table: "departments");

            migrationBuilder.DropColumn(
                name: "cost_centre",
                schema: "org",
                table: "departments");

            migrationBuilder.DropColumn(
                name: "default_shift_name",
                schema: "org",
                table: "departments");

            migrationBuilder.DropColumn(
                name: "department_type",
                schema: "org",
                table: "departments");

            migrationBuilder.DropColumn(
                name: "description",
                schema: "org",
                table: "departments");

            migrationBuilder.DropColumn(
                name: "effective_date",
                schema: "org",
                table: "departments");

            migrationBuilder.DropColumn(
                name: "escalation_level",
                schema: "org",
                table: "departments");

            migrationBuilder.DropColumn(
                name: "grace_minutes",
                schema: "org",
                table: "departments");

            migrationBuilder.DropColumn(
                name: "hod_effective_from",
                schema: "org",
                table: "departments");

            migrationBuilder.DropColumn(
                name: "hod_employee_id",
                schema: "org",
                table: "departments");

            migrationBuilder.DropColumn(
                name: "holiday_calendar_code",
                schema: "org",
                table: "departments");

            migrationBuilder.DropColumn(
                name: "leave_approver_employee_id",
                schema: "org",
                table: "departments");

            migrationBuilder.DropColumn(
                name: "od_approver_employee_id",
                schema: "org",
                table: "departments");

            migrationBuilder.DropColumn(
                name: "ot_approver_employee_id",
                schema: "org",
                table: "departments");

            migrationBuilder.DropColumn(
                name: "overtime_allowed",
                schema: "org",
                table: "departments");

            migrationBuilder.DropColumn(
                name: "regularization_approver_employee_id",
                schema: "org",
                table: "departments");

            migrationBuilder.DropColumn(
                name: "reporting_manager_employee_id",
                schema: "org",
                table: "departments");

            migrationBuilder.DropColumn(
                name: "weekly_off_config_json",
                schema: "org",
                table: "departments");

            migrationBuilder.DropColumn(
                name: "weekly_off_pattern",
                schema: "org",
                table: "departments");

            migrationBuilder.DropColumn(
                name: "wfh_allowed",
                schema: "org",
                table: "departments");

            migrationBuilder.DropColumn(
                name: "work_area",
                schema: "org",
                table: "departments");

            migrationBuilder.DropColumn(
                name: "work_end",
                schema: "org",
                table: "departments");

            migrationBuilder.DropColumn(
                name: "work_start",
                schema: "org",
                table: "departments");

            migrationBuilder.DropColumn(
                name: "cin",
                schema: "org",
                table: "companies");

            migrationBuilder.DropColumn(
                name: "company_type",
                schema: "org",
                table: "companies");

            migrationBuilder.DropColumn(
                name: "contact_number",
                schema: "org",
                table: "companies");

            migrationBuilder.DropColumn(
                name: "currency",
                schema: "org",
                table: "companies");

            migrationBuilder.DropColumn(
                name: "default_attendance_policy_code",
                schema: "org",
                table: "companies");

            migrationBuilder.DropColumn(
                name: "default_holiday_calendar_code",
                schema: "org",
                table: "companies");

            migrationBuilder.DropColumn(
                name: "default_leave_policy_code",
                schema: "org",
                table: "companies");

            migrationBuilder.DropColumn(
                name: "default_shift_name",
                schema: "org",
                table: "companies");

            migrationBuilder.DropColumn(
                name: "esic_employer_code",
                schema: "org",
                table: "companies");

            migrationBuilder.DropColumn(
                name: "factory_licence_no",
                schema: "org",
                table: "companies");

            migrationBuilder.DropColumn(
                name: "financial_year_start_month",
                schema: "org",
                table: "companies");

            migrationBuilder.DropColumn(
                name: "grace_minutes",
                schema: "org",
                table: "companies");

            migrationBuilder.DropColumn(
                name: "incorporation_date",
                schema: "org",
                table: "companies");

            migrationBuilder.DropColumn(
                name: "industry",
                schema: "org",
                table: "companies");

            migrationBuilder.DropColumn(
                name: "logo_file_id",
                schema: "org",
                table: "companies");

            migrationBuilder.DropColumn(
                name: "msme_udyam_reg_no",
                schema: "org",
                table: "companies");

            migrationBuilder.DropColumn(
                name: "official_email",
                schema: "org",
                table: "companies");

            migrationBuilder.DropColumn(
                name: "pan",
                schema: "org",
                table: "companies");

            migrationBuilder.DropColumn(
                name: "parent_company_id",
                schema: "org",
                table: "companies");

            migrationBuilder.DropColumn(
                name: "payroll_frequency",
                schema: "org",
                table: "companies");

            migrationBuilder.DropColumn(
                name: "pf_establishment_code",
                schema: "org",
                table: "companies");

            migrationBuilder.DropColumn(
                name: "professional_tax_reg_no",
                schema: "org",
                table: "companies");

            migrationBuilder.DropColumn(
                name: "salary_pay_day",
                schema: "org",
                table: "companies");

            migrationBuilder.DropColumn(
                name: "shops_establishment_reg_no",
                schema: "org",
                table: "companies");

            migrationBuilder.DropColumn(
                name: "tan",
                schema: "org",
                table: "companies");

            migrationBuilder.DropColumn(
                name: "website",
                schema: "org",
                table: "companies");

            migrationBuilder.DropColumn(
                name: "weekly_off_config_json",
                schema: "org",
                table: "companies");

            migrationBuilder.DropColumn(
                name: "weekly_off_pattern",
                schema: "org",
                table: "companies");

            migrationBuilder.DropColumn(
                name: "address_line1",
                schema: "org",
                table: "branches");

            migrationBuilder.DropColumn(
                name: "address_line2",
                schema: "org",
                table: "branches");

            migrationBuilder.DropColumn(
                name: "attendance_mode",
                schema: "org",
                table: "branches");

            migrationBuilder.DropColumn(
                name: "attendance_policy_code",
                schema: "org",
                table: "branches");

            migrationBuilder.DropColumn(
                name: "branch_type",
                schema: "org",
                table: "branches");

            migrationBuilder.DropColumn(
                name: "break_minutes",
                schema: "org",
                table: "branches");

            migrationBuilder.DropColumn(
                name: "city",
                schema: "org",
                table: "branches");

            migrationBuilder.DropColumn(
                name: "client_site_ref",
                schema: "org",
                table: "branches");

            migrationBuilder.DropColumn(
                name: "contact_email",
                schema: "org",
                table: "branches");

            migrationBuilder.DropColumn(
                name: "contact_phone",
                schema: "org",
                table: "branches");

            migrationBuilder.DropColumn(
                name: "cost_centre",
                schema: "org",
                table: "branches");

            migrationBuilder.DropColumn(
                name: "country",
                schema: "org",
                table: "branches");

            migrationBuilder.DropColumn(
                name: "default_shift_name",
                schema: "org",
                table: "branches");

            migrationBuilder.DropColumn(
                name: "district",
                schema: "org",
                table: "branches");

            migrationBuilder.DropColumn(
                name: "geofence_radius_meters",
                schema: "org",
                table: "branches");

            migrationBuilder.DropColumn(
                name: "grace_minutes",
                schema: "org",
                table: "branches");

            migrationBuilder.DropColumn(
                name: "holiday_calendar_code",
                schema: "org",
                table: "branches");

            migrationBuilder.DropColumn(
                name: "hr_employee_id",
                schema: "org",
                table: "branches");

            migrationBuilder.DropColumn(
                name: "latitude",
                schema: "org",
                table: "branches");

            migrationBuilder.DropColumn(
                name: "longitude",
                schema: "org",
                table: "branches");

            migrationBuilder.DropColumn(
                name: "manager_employee_id",
                schema: "org",
                table: "branches");

            migrationBuilder.DropColumn(
                name: "opening_date",
                schema: "org",
                table: "branches");

            migrationBuilder.DropColumn(
                name: "overtime_allowed",
                schema: "org",
                table: "branches");

            migrationBuilder.DropColumn(
                name: "payroll_day",
                schema: "org",
                table: "branches");

            migrationBuilder.DropColumn(
                name: "pin_code",
                schema: "org",
                table: "branches");

            migrationBuilder.DropColumn(
                name: "state",
                schema: "org",
                table: "branches");

            migrationBuilder.DropColumn(
                name: "weekly_off_config_json",
                schema: "org",
                table: "branches");

            migrationBuilder.DropColumn(
                name: "weekly_off_pattern",
                schema: "org",
                table: "branches");

            migrationBuilder.DropColumn(
                name: "work_end",
                schema: "org",
                table: "branches");

            migrationBuilder.DropColumn(
                name: "work_location_type",
                schema: "org",
                table: "branches");

            migrationBuilder.DropColumn(
                name: "work_start",
                schema: "org",
                table: "branches");
        }
    }
}
