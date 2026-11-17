using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hrms.Infrastructure.Persistence.Migrations.Tenant
{
    /// <inheritdoc />
    public partial class EmployeeProfileSections : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "hr");

            migrationBuilder.CreateTable(
                name: "employee_bank_accounts",
                schema: "hr",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    bank_name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    branch_name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    account_number_encrypted = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    account_last4 = table.Column<string>(type: "character varying(4)", maxLength: 4, nullable: false),
                    ifsc = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    holder_name = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: true),
                    is_primary = table.Column<bool>(type: "boolean", nullable: false),
                    effective_from = table.Column<DateOnly>(type: "date", nullable: false),
                    effective_to = table.Column<DateOnly>(type: "date", nullable: true),
                    verification_status = table.Column<int>(type: "integer", nullable: false),
                    verified_by = table.Column<Guid>(type: "uuid", nullable: true),
                    verified_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    verification_note = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    modified_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    modified_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_employee_bank_accounts", x => x.id);
                    table.ForeignKey(
                        name: "fk_employee_bank_accounts_employees_employee_id",
                        column: x => x.employee_id,
                        principalSchema: "org",
                        principalTable: "employees",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "employee_contacts",
                schema: "hr",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    name = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    relation = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    date_of_birth = table.Column<DateOnly>(type: "date", nullable: true),
                    phone = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    email = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    is_primary = table.Column<bool>(type: "boolean", nullable: false),
                    nominee_share_pct = table.Column<int>(type: "integer", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    modified_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    modified_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_employee_contacts", x => x.id);
                    table.CheckConstraint("ck_employee_contacts_kind", "kind IN ('emergency','family','nominee')");
                    table.CheckConstraint("ck_employee_contacts_share", "nominee_share_pct IS NULL OR (nominee_share_pct BETWEEN 1 AND 100)");
                    table.ForeignKey(
                        name: "fk_employee_contacts_employees_employee_id",
                        column: x => x.employee_id,
                        principalSchema: "org",
                        principalTable: "employees",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "employee_personal",
                schema: "hr",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    date_of_birth = table.Column<DateOnly>(type: "date", nullable: true),
                    gender = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    marital_status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    nationality = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    blood_group = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: true),
                    personal_email = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    personal_phone = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    current_address_json = table.Column<string>(type: "jsonb", nullable: true),
                    permanent_address_json = table.Column<string>(type: "jsonb", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    modified_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    modified_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_employee_personal", x => x.id);
                    table.ForeignKey(
                        name: "fk_employee_personal_employees_employee_id",
                        column: x => x.employee_id,
                        principalSchema: "org",
                        principalTable: "employees",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "employee_statutory",
                schema: "hr",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    pan_encrypted = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    pan_last4 = table.Column<string>(type: "character varying(4)", maxLength: 4, nullable: true),
                    uan = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    pf_number = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    pf_applicable = table.Column<bool>(type: "boolean", nullable: false),
                    esi_number = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    esi_applicable = table.Column<bool>(type: "boolean", nullable: false),
                    pt_state = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    lwf_applicable = table.Column<bool>(type: "boolean", nullable: false),
                    aadhaar_last4 = table.Column<string>(type: "character varying(4)", maxLength: 4, nullable: true),
                    passport_encrypted = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    passport_last4 = table.Column<string>(type: "character varying(4)", maxLength: 4, nullable: true),
                    tax_regime = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    modified_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    modified_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_employee_statutory", x => x.id);
                    table.ForeignKey(
                        name: "fk_employee_statutory_employees_employee_id",
                        column: x => x.employee_id,
                        principalSchema: "org",
                        principalTable: "employees",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ux_employee_bank_one_active_primary",
                schema: "hr",
                table: "employee_bank_accounts",
                column: "employee_id",
                unique: true,
                filter: "is_primary AND effective_to IS NULL");

            migrationBuilder.CreateIndex(
                name: "ux_employee_contacts_primary_emergency",
                schema: "hr",
                table: "employee_contacts",
                column: "employee_id",
                unique: true,
                filter: "kind = 'emergency' AND is_primary");

            migrationBuilder.CreateIndex(
                name: "ix_employee_personal_employee_id",
                schema: "hr",
                table: "employee_personal",
                column: "employee_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_employee_statutory_employee_id",
                schema: "hr",
                table: "employee_statutory",
                column: "employee_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "employee_bank_accounts",
                schema: "hr");

            migrationBuilder.DropTable(
                name: "employee_contacts",
                schema: "hr");

            migrationBuilder.DropTable(
                name: "employee_personal",
                schema: "hr");

            migrationBuilder.DropTable(
                name: "employee_statutory",
                schema: "hr");
        }
    }
}
