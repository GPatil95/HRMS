using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hrms.Infrastructure.Persistence.Migrations.Tenant
{
    /// <inheritdoc />
    public partial class LetterManagementV2 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_letter_templates_letter_type_id_version",
                schema: "hr",
                table: "letter_templates");

            migrationBuilder.DropCheckConstraint(
                name: "ck_employee_letters_status",
                schema: "hr",
                table: "employee_letters");

            migrationBuilder.AddColumn<string>(
                name: "number_prefix",
                schema: "hr",
                table: "letter_types",
                type: "character varying(40)",
                maxLength: 40,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "number_reset",
                schema: "hr",
                table: "letter_types",
                type: "character varying(10)",
                maxLength: 10,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<Guid>(
                name: "signatory_id",
                schema: "hr",
                table: "letter_types",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "branch_id",
                schema: "hr",
                table: "letter_templates",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "company_id",
                schema: "hr",
                table: "letter_templates",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "effective_from",
                schema: "hr",
                table: "letter_templates",
                type: "date",
                nullable: false,
                defaultValue: new DateOnly(1, 1, 1));

            migrationBuilder.AddColumn<DateOnly>(
                name: "effective_to",
                schema: "hr",
                table: "letter_templates",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "family_id",
                schema: "hr",
                table: "letter_templates",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "letterhead_id",
                schema: "hr",
                table: "letter_templates",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "published_at",
                schema: "hr",
                table: "letter_templates",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "published_by",
                schema: "hr",
                table: "letter_templates",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "retired_at",
                schema: "hr",
                table: "letter_templates",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "retired_by",
                schema: "hr",
                table: "letter_templates",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "status",
                schema: "hr",
                table: "letter_templates",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<Guid>(
                name: "supersedes_id",
                schema: "hr",
                table: "letter_templates",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "file_id",
                schema: "hr",
                table: "employee_letters",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "issue_date",
                schema: "hr",
                table: "employee_letters",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "issue_tz",
                schema: "hr",
                table: "employee_letters",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "letter_number",
                schema: "hr",
                table: "employee_letters",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "letterhead_snapshot",
                schema: "hr",
                table: "employee_letters",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "pdf_sha256",
                schema: "hr",
                table: "employee_letters",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "rendered_html",
                schema: "hr",
                table: "employee_letters",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "signatory_designation",
                schema: "hr",
                table: "employee_letters",
                type: "character varying(120)",
                maxLength: 120,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "signatory_name",
                schema: "hr",
                table: "employee_letters",
                type: "character varying(160)",
                maxLength: 160,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "supersedes_id",
                schema: "hr",
                table: "employee_letters",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "letter_counters",
                schema: "hr",
                columns: table => new
                {
                    scope_key = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    last_number = table.Column<long>(type: "bigint", nullable: false),
                    prefix = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_letter_counters", x => x.scope_key);
                });

            migrationBuilder.CreateTable(
                name: "letter_signatories",
                schema: "hr",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    designation = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    employee_id = table.Column<Guid>(type: "uuid", nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    modified_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    modified_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_letter_signatories", x => x.id);
                    table.ForeignKey(
                        name: "fk_letter_signatories_employees_employee_id",
                        column: x => x.employee_id,
                        principalSchema: "org",
                        principalTable: "employees",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "letter_template_families",
                schema: "hr",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    letter_type_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    modified_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    modified_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_letter_template_families", x => x.id);
                    table.ForeignKey(
                        name: "fk_letter_template_families_letter_types_letter_type_id",
                        column: x => x.letter_type_id,
                        principalSchema: "hr",
                        principalTable: "letter_types",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "letterheads",
                schema: "hr",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: true),
                    branch_id = table.Column<Guid>(type: "uuid", nullable: true),
                    logo_file_id = table.Column<Guid>(type: "uuid", nullable: true),
                    header_html = table.Column<string>(type: "text", nullable: true),
                    footer_html = table.Column<string>(type: "text", nullable: true),
                    margin_mm = table.Column<int>(type: "integer", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    modified_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    modified_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_letterheads", x => x.id);
                    table.ForeignKey(
                        name: "fk_letterheads_branches_branch_id",
                        column: x => x.branch_id,
                        principalSchema: "org",
                        principalTable: "branches",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_letterheads_companies_company_id",
                        column: x => x.company_id,
                        principalSchema: "org",
                        principalTable: "companies",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_letterheads_files_logo_file_id",
                        column: x => x.logo_file_id,
                        principalSchema: "files",
                        principalTable: "files",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            // Backfill: one family per existing template (family id = template id for traceability),
            // then family/status/effective defaults. Runs before the unique (family_id, version) index and FK.
            migrationBuilder.Sql("""
                insert into hr.letter_template_families (id, letter_type_id, name, created_at)
                select id, letter_type_id, name, now() from hr.letter_templates;

                update hr.letter_templates
                set family_id = id,
                    status = case when is_active then 'published' else 'draft' end,
                    effective_from = '2000-01-01'
                where family_id = '00000000-0000-0000-0000-000000000000';

                update hr.letter_types set number_prefix = 'LTR' where number_prefix = '';
                update hr.letter_types set number_reset = 'yearly' where number_reset = '';

                update hr.employee_letters set letter_number = 'LEGACY-' || id::text where letter_number = '';
                """);

            migrationBuilder.CreateIndex(
                name: "ix_letter_types_signatory_id",
                schema: "hr",
                table: "letter_types",
                column: "signatory_id");

            migrationBuilder.CreateIndex(
                name: "ix_letter_templates_branch_id",
                schema: "hr",
                table: "letter_templates",
                column: "branch_id");

            migrationBuilder.CreateIndex(
                name: "ix_letter_templates_company_id",
                schema: "hr",
                table: "letter_templates",
                column: "company_id");

            migrationBuilder.CreateIndex(
                name: "ix_letter_templates_family_id_version",
                schema: "hr",
                table: "letter_templates",
                columns: new[] { "family_id", "version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_letter_templates_letter_type_id",
                schema: "hr",
                table: "letter_templates",
                column: "letter_type_id");

            migrationBuilder.CreateIndex(
                name: "ix_letter_templates_letterhead_id",
                schema: "hr",
                table: "letter_templates",
                column: "letterhead_id");

            migrationBuilder.CreateIndex(
                name: "ix_letter_templates_supersedes_id",
                schema: "hr",
                table: "letter_templates",
                column: "supersedes_id");

            migrationBuilder.AddCheckConstraint(
                name: "ck_letter_templates_status",
                schema: "hr",
                table: "letter_templates",
                sql: "status IN ('draft','published','retired')");

            migrationBuilder.CreateIndex(
                name: "ix_employee_letters_file_id",
                schema: "hr",
                table: "employee_letters",
                column: "file_id");

            migrationBuilder.CreateIndex(
                name: "ix_employee_letters_letter_number",
                schema: "hr",
                table: "employee_letters",
                column: "letter_number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_employee_letters_supersedes_id",
                schema: "hr",
                table: "employee_letters",
                column: "supersedes_id");

            migrationBuilder.AddCheckConstraint(
                name: "ck_employee_letters_status",
                schema: "hr",
                table: "employee_letters",
                sql: "status IN ('issuing','issued','generation_failed','revoked','superseded')");

            migrationBuilder.CreateIndex(
                name: "ix_letter_signatories_employee_id",
                schema: "hr",
                table: "letter_signatories",
                column: "employee_id");

            migrationBuilder.CreateIndex(
                name: "ix_letter_template_families_letter_type_id",
                schema: "hr",
                table: "letter_template_families",
                column: "letter_type_id");

            migrationBuilder.CreateIndex(
                name: "ix_letterheads_branch_id",
                schema: "hr",
                table: "letterheads",
                column: "branch_id");

            migrationBuilder.CreateIndex(
                name: "ix_letterheads_company_id",
                schema: "hr",
                table: "letterheads",
                column: "company_id");

            migrationBuilder.CreateIndex(
                name: "ix_letterheads_logo_file_id",
                schema: "hr",
                table: "letterheads",
                column: "logo_file_id");

            migrationBuilder.AddForeignKey(
                name: "fk_employee_letters_employee_letters_supersedes_id",
                schema: "hr",
                table: "employee_letters",
                column: "supersedes_id",
                principalSchema: "hr",
                principalTable: "employee_letters",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_employee_letters_files_file_id",
                schema: "hr",
                table: "employee_letters",
                column: "file_id",
                principalSchema: "files",
                principalTable: "files",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_letter_templates_branches_branch_id",
                schema: "hr",
                table: "letter_templates",
                column: "branch_id",
                principalSchema: "org",
                principalTable: "branches",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_letter_templates_companies_company_id",
                schema: "hr",
                table: "letter_templates",
                column: "company_id",
                principalSchema: "org",
                principalTable: "companies",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_letter_templates_letter_template_families_family_id",
                schema: "hr",
                table: "letter_templates",
                column: "family_id",
                principalSchema: "hr",
                principalTable: "letter_template_families",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_letter_templates_letter_templates_supersedes_id",
                schema: "hr",
                table: "letter_templates",
                column: "supersedes_id",
                principalSchema: "hr",
                principalTable: "letter_templates",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_letter_templates_letterheads_letterhead_id",
                schema: "hr",
                table: "letter_templates",
                column: "letterhead_id",
                principalSchema: "hr",
                principalTable: "letterheads",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_letter_types_letter_signatories_signatory_id",
                schema: "hr",
                table: "letter_types",
                column: "signatory_id",
                principalSchema: "hr",
                principalTable: "letter_signatories",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            // One published template per (type, applicability level, effective window): the COALESCE'd
            // keys let tenant-wide (null,null), company-level and branch-level coexist independently.
            migrationBuilder.Sql("""
                alter table hr.letter_templates
                add constraint ex_letter_templates_no_overlap
                exclude using gist (
                  letter_type_id with =,
                  coalesce(company_id, '00000000-0000-0000-0000-000000000000'::uuid) with =,
                  coalesce(branch_id,   '00000000-0000-0000-0000-000000000000'::uuid) with =,
                  daterange(effective_from, coalesce(effective_to, 'infinity'::date), '[)') with &&
                ) where (status = 'published');
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("alter table hr.letter_templates drop constraint ex_letter_templates_no_overlap;");

            migrationBuilder.DropForeignKey(
                name: "fk_employee_letters_employee_letters_supersedes_id",
                schema: "hr",
                table: "employee_letters");

            migrationBuilder.DropForeignKey(
                name: "fk_employee_letters_files_file_id",
                schema: "hr",
                table: "employee_letters");

            migrationBuilder.DropForeignKey(
                name: "fk_letter_templates_branches_branch_id",
                schema: "hr",
                table: "letter_templates");

            migrationBuilder.DropForeignKey(
                name: "fk_letter_templates_companies_company_id",
                schema: "hr",
                table: "letter_templates");

            migrationBuilder.DropForeignKey(
                name: "fk_letter_templates_letter_template_families_family_id",
                schema: "hr",
                table: "letter_templates");

            migrationBuilder.DropForeignKey(
                name: "fk_letter_templates_letter_templates_supersedes_id",
                schema: "hr",
                table: "letter_templates");

            migrationBuilder.DropForeignKey(
                name: "fk_letter_templates_letterheads_letterhead_id",
                schema: "hr",
                table: "letter_templates");

            migrationBuilder.DropForeignKey(
                name: "fk_letter_types_letter_signatories_signatory_id",
                schema: "hr",
                table: "letter_types");

            migrationBuilder.DropTable(
                name: "letter_counters",
                schema: "hr");

            migrationBuilder.DropTable(
                name: "letter_signatories",
                schema: "hr");

            migrationBuilder.DropTable(
                name: "letter_template_families",
                schema: "hr");

            migrationBuilder.DropTable(
                name: "letterheads",
                schema: "hr");

            migrationBuilder.DropIndex(
                name: "ix_letter_types_signatory_id",
                schema: "hr",
                table: "letter_types");

            migrationBuilder.DropIndex(
                name: "ix_letter_templates_branch_id",
                schema: "hr",
                table: "letter_templates");

            migrationBuilder.DropIndex(
                name: "ix_letter_templates_company_id",
                schema: "hr",
                table: "letter_templates");

            migrationBuilder.DropIndex(
                name: "ix_letter_templates_family_id_version",
                schema: "hr",
                table: "letter_templates");

            migrationBuilder.DropIndex(
                name: "ix_letter_templates_letter_type_id",
                schema: "hr",
                table: "letter_templates");

            migrationBuilder.DropIndex(
                name: "ix_letter_templates_letterhead_id",
                schema: "hr",
                table: "letter_templates");

            migrationBuilder.DropIndex(
                name: "ix_letter_templates_supersedes_id",
                schema: "hr",
                table: "letter_templates");

            migrationBuilder.DropCheckConstraint(
                name: "ck_letter_templates_status",
                schema: "hr",
                table: "letter_templates");

            migrationBuilder.DropIndex(
                name: "ix_employee_letters_file_id",
                schema: "hr",
                table: "employee_letters");

            migrationBuilder.DropIndex(
                name: "ix_employee_letters_letter_number",
                schema: "hr",
                table: "employee_letters");

            migrationBuilder.DropIndex(
                name: "ix_employee_letters_supersedes_id",
                schema: "hr",
                table: "employee_letters");

            migrationBuilder.DropCheckConstraint(
                name: "ck_employee_letters_status",
                schema: "hr",
                table: "employee_letters");

            migrationBuilder.DropColumn(
                name: "number_prefix",
                schema: "hr",
                table: "letter_types");

            migrationBuilder.DropColumn(
                name: "number_reset",
                schema: "hr",
                table: "letter_types");

            migrationBuilder.DropColumn(
                name: "signatory_id",
                schema: "hr",
                table: "letter_types");

            migrationBuilder.DropColumn(
                name: "branch_id",
                schema: "hr",
                table: "letter_templates");

            migrationBuilder.DropColumn(
                name: "company_id",
                schema: "hr",
                table: "letter_templates");

            migrationBuilder.DropColumn(
                name: "effective_from",
                schema: "hr",
                table: "letter_templates");

            migrationBuilder.DropColumn(
                name: "effective_to",
                schema: "hr",
                table: "letter_templates");

            migrationBuilder.DropColumn(
                name: "family_id",
                schema: "hr",
                table: "letter_templates");

            migrationBuilder.DropColumn(
                name: "letterhead_id",
                schema: "hr",
                table: "letter_templates");

            migrationBuilder.DropColumn(
                name: "published_at",
                schema: "hr",
                table: "letter_templates");

            migrationBuilder.DropColumn(
                name: "published_by",
                schema: "hr",
                table: "letter_templates");

            migrationBuilder.DropColumn(
                name: "retired_at",
                schema: "hr",
                table: "letter_templates");

            migrationBuilder.DropColumn(
                name: "retired_by",
                schema: "hr",
                table: "letter_templates");

            migrationBuilder.DropColumn(
                name: "status",
                schema: "hr",
                table: "letter_templates");

            migrationBuilder.DropColumn(
                name: "supersedes_id",
                schema: "hr",
                table: "letter_templates");

            migrationBuilder.DropColumn(
                name: "file_id",
                schema: "hr",
                table: "employee_letters");

            migrationBuilder.DropColumn(
                name: "issue_date",
                schema: "hr",
                table: "employee_letters");

            migrationBuilder.DropColumn(
                name: "issue_tz",
                schema: "hr",
                table: "employee_letters");

            migrationBuilder.DropColumn(
                name: "letter_number",
                schema: "hr",
                table: "employee_letters");

            migrationBuilder.DropColumn(
                name: "letterhead_snapshot",
                schema: "hr",
                table: "employee_letters");

            migrationBuilder.DropColumn(
                name: "pdf_sha256",
                schema: "hr",
                table: "employee_letters");

            migrationBuilder.DropColumn(
                name: "rendered_html",
                schema: "hr",
                table: "employee_letters");

            migrationBuilder.DropColumn(
                name: "signatory_designation",
                schema: "hr",
                table: "employee_letters");

            migrationBuilder.DropColumn(
                name: "signatory_name",
                schema: "hr",
                table: "employee_letters");

            migrationBuilder.DropColumn(
                name: "supersedes_id",
                schema: "hr",
                table: "employee_letters");

            migrationBuilder.CreateIndex(
                name: "ix_letter_templates_letter_type_id_version",
                schema: "hr",
                table: "letter_templates",
                columns: new[] { "letter_type_id", "version" },
                unique: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_employee_letters_status",
                schema: "hr",
                table: "employee_letters",
                sql: "status IN ('issued','revoked')");
        }
    }
}
