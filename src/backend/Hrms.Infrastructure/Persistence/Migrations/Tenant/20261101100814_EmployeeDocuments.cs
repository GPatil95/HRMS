using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hrms.Infrastructure.Persistence.Migrations.Tenant
{
    /// <inheritdoc />
    public partial class EmployeeDocuments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "document_categories",
                schema: "hr",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    description = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    required_for_onboarding = table.Column<bool>(type: "boolean", nullable: false),
                    retention_years = table.Column<int>(type: "integer", nullable: true),
                    confidential = table.Column<bool>(type: "boolean", nullable: false),
                    track_expiry = table.Column<bool>(type: "boolean", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    modified_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    modified_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_document_categories", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "letter_types",
                schema: "hr",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    description = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    signatory_employee_id = table.Column<Guid>(type: "uuid", nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    modified_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    modified_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_letter_types", x => x.id);
                    table.ForeignKey(
                        name: "fk_letter_types_employees_signatory_employee_id",
                        column: x => x.signatory_employee_id,
                        principalSchema: "org",
                        principalTable: "employees",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "employee_documents",
                schema: "hr",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    category_id = table.Column<Guid>(type: "uuid", nullable: false),
                    file_id = table.Column<Guid>(type: "uuid", nullable: false),
                    label = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: true),
                    document_number = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    issued_on = table.Column<DateOnly>(type: "date", nullable: true),
                    expires_on = table.Column<DateOnly>(type: "date", nullable: true),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    verified_by = table.Column<Guid>(type: "uuid", nullable: true),
                    verified_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    verification_note = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    replaced_by_id = table.Column<Guid>(type: "uuid", nullable: true),
                    version = table.Column<int>(type: "integer", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    modified_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    modified_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_employee_documents", x => x.id);
                    table.CheckConstraint("ck_employee_documents_status", "status IN ('uploaded','verified','rejected','replaced','expired')");
                    table.ForeignKey(
                        name: "fk_employee_documents_document_categories_category_id",
                        column: x => x.category_id,
                        principalSchema: "hr",
                        principalTable: "document_categories",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_employee_documents_employee_documents_replaced_by_id",
                        column: x => x.replaced_by_id,
                        principalSchema: "hr",
                        principalTable: "employee_documents",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_employee_documents_employees_employee_id",
                        column: x => x.employee_id,
                        principalSchema: "org",
                        principalTable: "employees",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_employee_documents_files_file_id",
                        column: x => x.file_id,
                        principalSchema: "files",
                        principalTable: "files",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_employee_documents_users_verified_by",
                        column: x => x.verified_by,
                        principalSchema: "identity",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "letter_templates",
                schema: "hr",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    letter_type_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    subject = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    body = table.Column<string>(type: "text", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    modified_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    modified_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_letter_templates", x => x.id);
                    table.ForeignKey(
                        name: "fk_letter_templates_letter_types_letter_type_id",
                        column: x => x.letter_type_id,
                        principalSchema: "hr",
                        principalTable: "letter_types",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "employee_letters",
                schema: "hr",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    letter_type_id = table.Column<Guid>(type: "uuid", nullable: false),
                    letter_template_id = table.Column<Guid>(type: "uuid", nullable: true),
                    template_version = table.Column<int>(type: "integer", nullable: false),
                    subject = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    rendered_text = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    issued_by = table.Column<Guid>(type: "uuid", nullable: false),
                    issued_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    revoked_by = table.Column<Guid>(type: "uuid", nullable: true),
                    revoked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    revoke_reason = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    modified_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    modified_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_employee_letters", x => x.id);
                    table.CheckConstraint("ck_employee_letters_status", "status IN ('issued','revoked')");
                    table.ForeignKey(
                        name: "fk_employee_letters_employees_employee_id",
                        column: x => x.employee_id,
                        principalSchema: "org",
                        principalTable: "employees",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_employee_letters_letter_templates_letter_template_id",
                        column: x => x.letter_template_id,
                        principalSchema: "hr",
                        principalTable: "letter_templates",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_employee_letters_letter_types_letter_type_id",
                        column: x => x.letter_type_id,
                        principalSchema: "hr",
                        principalTable: "letter_types",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_employee_letters_users_issued_by",
                        column: x => x.issued_by,
                        principalSchema: "identity",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_employee_letters_users_revoked_by",
                        column: x => x.revoked_by,
                        principalSchema: "identity",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_document_categories_code",
                schema: "hr",
                table: "document_categories",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_employee_documents_category_id",
                schema: "hr",
                table: "employee_documents",
                column: "category_id");

            migrationBuilder.CreateIndex(
                name: "ix_employee_documents_employee_id",
                schema: "hr",
                table: "employee_documents",
                column: "employee_id");

            migrationBuilder.CreateIndex(
                name: "ix_employee_documents_expires_on",
                schema: "hr",
                table: "employee_documents",
                column: "expires_on",
                filter: "expires_on IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_employee_documents_file_id",
                schema: "hr",
                table: "employee_documents",
                column: "file_id");

            migrationBuilder.CreateIndex(
                name: "ix_employee_documents_replaced_by_id",
                schema: "hr",
                table: "employee_documents",
                column: "replaced_by_id");

            migrationBuilder.CreateIndex(
                name: "ix_employee_documents_verified_by",
                schema: "hr",
                table: "employee_documents",
                column: "verified_by");

            migrationBuilder.CreateIndex(
                name: "ix_employee_letters_employee_id",
                schema: "hr",
                table: "employee_letters",
                column: "employee_id");

            migrationBuilder.CreateIndex(
                name: "ix_employee_letters_issued_by",
                schema: "hr",
                table: "employee_letters",
                column: "issued_by");

            migrationBuilder.CreateIndex(
                name: "ix_employee_letters_letter_template_id",
                schema: "hr",
                table: "employee_letters",
                column: "letter_template_id");

            migrationBuilder.CreateIndex(
                name: "ix_employee_letters_letter_type_id",
                schema: "hr",
                table: "employee_letters",
                column: "letter_type_id");

            migrationBuilder.CreateIndex(
                name: "ix_employee_letters_revoked_by",
                schema: "hr",
                table: "employee_letters",
                column: "revoked_by");

            migrationBuilder.CreateIndex(
                name: "ix_letter_templates_letter_type_id_version",
                schema: "hr",
                table: "letter_templates",
                columns: new[] { "letter_type_id", "version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_letter_types_code",
                schema: "hr",
                table: "letter_types",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_letter_types_signatory_employee_id",
                schema: "hr",
                table: "letter_types",
                column: "signatory_employee_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "employee_documents",
                schema: "hr");

            migrationBuilder.DropTable(
                name: "employee_letters",
                schema: "hr");

            migrationBuilder.DropTable(
                name: "document_categories",
                schema: "hr");

            migrationBuilder.DropTable(
                name: "letter_templates",
                schema: "hr");

            migrationBuilder.DropTable(
                name: "letter_types",
                schema: "hr");
        }
    }
}
