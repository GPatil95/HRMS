using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hrms.Infrastructure.Persistence.Migrations.Tenant
{
    /// <inheritdoc />
    public partial class EmployeeSalary : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "salary_components",
                schema: "hr",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    name = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    payslip_name = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: true),
                    taxable = table.Column<bool>(type: "boolean", nullable: false),
                    pro_rata = table.Column<bool>(type: "boolean", nullable: false),
                    part_of_ctc = table.Column<bool>(type: "boolean", nullable: false),
                    part_of_gross = table.Column<bool>(type: "boolean", nullable: false),
                    is_statutory = table.Column<bool>(type: "boolean", nullable: false),
                    is_basic = table.Column<bool>(type: "boolean", nullable: false),
                    display_order = table.Column<int>(type: "integer", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    row_version = table.Column<long>(type: "bigint", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    modified_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    modified_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_salary_components", x => x.id);
                    table.CheckConstraint("ck_salary_components_type", "type IN ('earning','deduction','benefit','statutory','reimbursement')");
                });

            migrationBuilder.CreateTable(
                name: "salary_structures",
                schema: "hr",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    name = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    row_version = table.Column<long>(type: "bigint", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    modified_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    modified_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_salary_structures", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "employee_salary_assignments",
                schema: "hr",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    salary_structure_id = table.Column<Guid>(type: "uuid", nullable: false),
                    ctc_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    currency = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: true),
                    effective_from = table.Column<DateOnly>(type: "date", nullable: false),
                    effective_to = table.Column<DateOnly>(type: "date", nullable: true),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    lifecycle_event_id = table.Column<Guid>(type: "uuid", nullable: true),
                    requested_by = table.Column<Guid>(type: "uuid", nullable: true),
                    requested_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    approved_by = table.Column<Guid>(type: "uuid", nullable: true),
                    approved_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    row_version = table.Column<long>(type: "bigint", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    modified_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    modified_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_employee_salary_assignments", x => x.id);
                    table.CheckConstraint("ck_employee_salary_status", "status IN ('draft','submitted','approved','active','closed','rejected','cancelled')");
                    table.ForeignKey(
                        name: "fk_employee_salary_assignments_employees_employee_id",
                        column: x => x.employee_id,
                        principalSchema: "org",
                        principalTable: "employees",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_employee_salary_assignments_lifecycle_events_lifecycle_even",
                        column: x => x.lifecycle_event_id,
                        principalSchema: "org",
                        principalTable: "lifecycle_events",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_employee_salary_assignments_salary_structures_salary_struct",
                        column: x => x.salary_structure_id,
                        principalSchema: "hr",
                        principalTable: "salary_structures",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_employee_salary_assignments_users_approved_by",
                        column: x => x.approved_by,
                        principalSchema: "identity",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_employee_salary_assignments_users_requested_by",
                        column: x => x.requested_by,
                        principalSchema: "identity",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "salary_structure_lines",
                schema: "hr",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    salary_structure_id = table.Column<Guid>(type: "uuid", nullable: false),
                    salary_component_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sequence = table.Column<int>(type: "integer", nullable: false),
                    calc_type = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    percent = table.Column<decimal>(type: "numeric(9,4)", precision: 9, scale: 4, nullable: true),
                    formula = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_salary_structure_lines", x => x.id);
                    table.CheckConstraint("ck_salary_structure_lines_calc", "calc_type IN ('fixed','percent_of_basic','percent_of_gross','formula')");
                    table.ForeignKey(
                        name: "fk_salary_structure_lines_salary_components_salary_component_id",
                        column: x => x.salary_component_id,
                        principalSchema: "hr",
                        principalTable: "salary_components",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_salary_structure_lines_salary_structures_salary_structure_id",
                        column: x => x.salary_structure_id,
                        principalSchema: "hr",
                        principalTable: "salary_structures",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "employee_salary_lines",
                schema: "hr",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    employee_salary_assignment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    salary_component_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sequence = table.Column<int>(type: "integer", nullable: false),
                    calc_type = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    percent = table.Column<decimal>(type: "numeric(9,4)", precision: 9, scale: 4, nullable: true),
                    formula = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    resolved_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_employee_salary_lines", x => x.id);
                    table.CheckConstraint("ck_employee_salary_lines_calc", "calc_type IN ('fixed','percent_of_basic','percent_of_gross','formula')");
                    table.ForeignKey(
                        name: "fk_employee_salary_lines_employee_salary_assignments_employee_",
                        column: x => x.employee_salary_assignment_id,
                        principalSchema: "hr",
                        principalTable: "employee_salary_assignments",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_employee_salary_lines_salary_components_salary_component_id",
                        column: x => x.salary_component_id,
                        principalSchema: "hr",
                        principalTable: "salary_components",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_employee_salary_assignments_approved_by",
                schema: "hr",
                table: "employee_salary_assignments",
                column: "approved_by");

            migrationBuilder.CreateIndex(
                name: "ix_employee_salary_assignments_lifecycle_event_id",
                schema: "hr",
                table: "employee_salary_assignments",
                column: "lifecycle_event_id");

            migrationBuilder.CreateIndex(
                name: "ix_employee_salary_assignments_requested_by",
                schema: "hr",
                table: "employee_salary_assignments",
                column: "requested_by");

            migrationBuilder.CreateIndex(
                name: "ix_employee_salary_assignments_salary_structure_id",
                schema: "hr",
                table: "employee_salary_assignments",
                column: "salary_structure_id");

            migrationBuilder.CreateIndex(
                name: "ix_employee_salary_assignments_status_effective_from",
                schema: "hr",
                table: "employee_salary_assignments",
                columns: new[] { "status", "effective_from" });

            migrationBuilder.CreateIndex(
                name: "ux_employee_salary_one_active",
                schema: "hr",
                table: "employee_salary_assignments",
                column: "employee_id",
                unique: true,
                filter: "status = 'active'");

            migrationBuilder.CreateIndex(
                name: "ix_employee_salary_lines_employee_salary_assignment_id",
                schema: "hr",
                table: "employee_salary_lines",
                column: "employee_salary_assignment_id");

            migrationBuilder.CreateIndex(
                name: "ix_employee_salary_lines_salary_component_id",
                schema: "hr",
                table: "employee_salary_lines",
                column: "salary_component_id");

            migrationBuilder.CreateIndex(
                name: "ix_salary_components_code",
                schema: "hr",
                table: "salary_components",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_salary_structure_lines_salary_component_id",
                schema: "hr",
                table: "salary_structure_lines",
                column: "salary_component_id");

            migrationBuilder.CreateIndex(
                name: "ix_salary_structure_lines_salary_structure_id_salary_component",
                schema: "hr",
                table: "salary_structure_lines",
                columns: new[] { "salary_structure_id", "salary_component_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_salary_structures_code",
                schema: "hr",
                table: "salary_structures",
                column: "code",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "employee_salary_lines",
                schema: "hr");

            migrationBuilder.DropTable(
                name: "salary_structure_lines",
                schema: "hr");

            migrationBuilder.DropTable(
                name: "employee_salary_assignments",
                schema: "hr");

            migrationBuilder.DropTable(
                name: "salary_components",
                schema: "hr");

            migrationBuilder.DropTable(
                name: "salary_structures",
                schema: "hr");
        }
    }
}
