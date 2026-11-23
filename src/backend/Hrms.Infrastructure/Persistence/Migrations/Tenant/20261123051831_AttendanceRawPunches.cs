using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hrms.Infrastructure.Persistence.Migrations.Tenant
{
    /// <inheritdoc />
    public partial class AttendanceRawPunches : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "devices",
                schema: "att",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    name = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    vendor = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    branch_id = table.Column<Guid>(type: "uuid", nullable: true),
                    protocol = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    timezone_mode = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    device_timezone = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    token_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    credential_ref = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    last_seen_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    modified_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    modified_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_devices", x => x.id);
                    table.CheckConstraint("ck_devices_protocol", "protocol IN ('push_api','pull_poll','file_import')");
                    table.CheckConstraint("ck_devices_tz_mode", "timezone_mode IN ('utc','device_local')");
                    table.CheckConstraint("ck_devices_tz_required", "timezone_mode <> 'device_local' OR device_timezone IS NOT NULL");
                    table.ForeignKey(
                        name: "fk_devices_branches_branch_id",
                        column: x => x.branch_id,
                        principalSchema: "org",
                        principalTable: "branches",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "device_user_map",
                schema: "att",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    device_id = table.Column<Guid>(type: "uuid", nullable: false),
                    device_user_code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    effective_from = table.Column<DateOnly>(type: "date", nullable: false),
                    effective_to = table.Column<DateOnly>(type: "date", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    modified_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    modified_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_device_user_map", x => x.id);
                    table.ForeignKey(
                        name: "fk_device_user_map_devices_device_id",
                        column: x => x.device_id,
                        principalSchema: "att",
                        principalTable: "devices",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_device_user_map_employees_employee_id",
                        column: x => x.employee_id,
                        principalSchema: "org",
                        principalTable: "employees",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ingest_batches",
                schema: "att",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    source = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    device_id = table.Column<Guid>(type: "uuid", nullable: true),
                    file_id = table.Column<Guid>(type: "uuid", nullable: true),
                    idempotency_key = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    payload_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    received_count = table.Column<int>(type: "integer", nullable: false),
                    accepted_count = table.Column<int>(type: "integer", nullable: false),
                    duplicate_count = table.Column<int>(type: "integer", nullable: false),
                    rejected_count = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    error_summary = table.Column<string>(type: "jsonb", nullable: true),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    modified_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    modified_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ingest_batches", x => x.id);
                    table.CheckConstraint("ck_ingest_batches_source", "source IN ('device','self','manual','import')");
                    table.CheckConstraint("ck_ingest_batches_status", "status IN ('processing','completed','failed')");
                    table.ForeignKey(
                        name: "fk_ingest_batches_devices_device_id",
                        column: x => x.device_id,
                        principalSchema: "att",
                        principalTable: "devices",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_ingest_batches_files_file_id",
                        column: x => x.file_id,
                        principalSchema: "files",
                        principalTable: "files",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "punches",
                schema: "att",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    employee_id = table.Column<Guid>(type: "uuid", nullable: true),
                    punched_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    local_wall_time = table.Column<DateTime>(type: "timestamp", nullable: false),
                    source = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    direction = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    device_id = table.Column<Guid>(type: "uuid", nullable: true),
                    device_user_code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    ingest_batch_id = table.Column<Guid>(type: "uuid", nullable: true),
                    dedup_key = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    geo_lat = table.Column<double>(type: "double precision", nullable: true),
                    geo_lng = table.Column<double>(type: "double precision", nullable: true),
                    geo_accuracy_m = table.Column<int>(type: "integer", nullable: true),
                    ip_address = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    user_agent = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    ref_request_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    note = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    modified_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    modified_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_punches", x => x.id);
                    table.CheckConstraint("ck_punches_direction", "direction IN ('in','out','unknown')");
                    table.CheckConstraint("ck_punches_manual_note", "source <> 'manual' OR note IS NOT NULL");
                    table.CheckConstraint("ck_punches_source", "source IN ('device','self','manual','import','regularization')");
                    table.ForeignKey(
                        name: "fk_punches_devices_device_id",
                        column: x => x.device_id,
                        principalSchema: "att",
                        principalTable: "devices",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_punches_employees_employee_id",
                        column: x => x.employee_id,
                        principalSchema: "org",
                        principalTable: "employees",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_punches_ingest_batches_ingest_batch_id",
                        column: x => x.ingest_batch_id,
                        principalSchema: "att",
                        principalTable: "ingest_batches",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "punch_voids",
                schema: "att",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    punch_id = table.Column<Guid>(type: "uuid", nullable: false),
                    reason = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    modified_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    modified_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_punch_voids", x => x.id);
                    table.ForeignKey(
                        name: "fk_punch_voids_punches_punch_id",
                        column: x => x.punch_id,
                        principalSchema: "att",
                        principalTable: "punches",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_device_user_map_device_id_device_user_code_effective_from",
                schema: "att",
                table: "device_user_map",
                columns: new[] { "device_id", "device_user_code", "effective_from" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_device_user_map_employee_id",
                schema: "att",
                table: "device_user_map",
                column: "employee_id");

            migrationBuilder.CreateIndex(
                name: "ix_devices_branch_id",
                schema: "att",
                table: "devices",
                column: "branch_id");

            migrationBuilder.CreateIndex(
                name: "ix_devices_code",
                schema: "att",
                table: "devices",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_devices_token_hash",
                schema: "att",
                table: "devices",
                column: "token_hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_ingest_batches_device_id",
                schema: "att",
                table: "ingest_batches",
                column: "device_id");

            migrationBuilder.CreateIndex(
                name: "ix_ingest_batches_file_id",
                schema: "att",
                table: "ingest_batches",
                column: "file_id");

            migrationBuilder.CreateIndex(
                name: "ix_ingest_batches_idempotency_key",
                schema: "att",
                table: "ingest_batches",
                column: "idempotency_key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_punch_voids_punch_id",
                schema: "att",
                table: "punch_voids",
                column: "punch_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_punches_dedup_key",
                schema: "att",
                table: "punches",
                column: "dedup_key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_punches_device_id_device_user_code",
                schema: "att",
                table: "punches",
                columns: new[] { "device_id", "device_user_code" });

            migrationBuilder.CreateIndex(
                name: "ix_punches_employee_id_punched_at",
                schema: "att",
                table: "punches",
                columns: new[] { "employee_id", "punched_at" });

            migrationBuilder.CreateIndex(
                name: "ix_punches_ingest_batch_id",
                schema: "att",
                table: "punches",
                column: "ingest_batch_id");

            // -- Supplementary DDL not expressible in the EF model --

            // Volume-optimized time index for the raw punch store.
            migrationBuilder.Sql("CREATE INDEX ix_punches_punched_at_brin ON att.punches USING brin (punched_at);");

            // No-overlap guarantee for effective-dated device user mappings.
            migrationBuilder.Sql("""
                CREATE EXTENSION IF NOT EXISTS btree_gist;
                ALTER TABLE att.device_user_map ADD CONSTRAINT ex_device_user_map_no_overlap
                    EXCLUDE USING gist (
                        device_id WITH =,
                        device_user_code WITH =,
                        daterange(effective_from, COALESCE(effective_to, DATE '9999-12-31'), '[]') WITH &&
                    );
                """);

            // Append-only guard: DELETE always rejected; UPDATE only permitted for the
            // NULL -> populated employee_id back-fill performed by the attendance.device.map job.
            migrationBuilder.Sql("""
                CREATE OR REPLACE FUNCTION att.punches_immutable() RETURNS trigger AS $$
                BEGIN
                    IF TG_OP = 'DELETE' THEN
                        RAISE EXCEPTION 'att.punches is append-only (punch %)', OLD.id;
                    END IF;
                    IF OLD.employee_id IS NOT NULL OR NEW.employee_id IS NULL OR
                       NEW.punched_at <> OLD.punched_at OR NEW.local_wall_time <> OLD.local_wall_time OR
                       NEW.source <> OLD.source OR NEW.direction <> OLD.direction OR
                       NEW.device_id IS DISTINCT FROM OLD.device_id OR
                       NEW.device_user_code IS DISTINCT FROM OLD.device_user_code OR
                       NEW.ingest_batch_id IS DISTINCT FROM OLD.ingest_batch_id OR
                       NEW.dedup_key <> OLD.dedup_key OR
                       NEW.geo_lat IS DISTINCT FROM OLD.geo_lat OR
                       NEW.geo_lng IS DISTINCT FROM OLD.geo_lng OR
                       NEW.geo_accuracy_m IS DISTINCT FROM OLD.geo_accuracy_m OR
                       NEW.ip_address IS DISTINCT FROM OLD.ip_address OR
                       NEW.user_agent IS DISTINCT FROM OLD.user_agent OR
                       NEW.ref_request_id IS DISTINCT FROM OLD.ref_request_id OR
                       NEW.note IS DISTINCT FROM OLD.note
                    THEN
                        RAISE EXCEPTION 'att.punches is append-only: only employee_id back-fill is permitted (punch %)', OLD.id;
                    END IF;
                    RETURN NEW;
                END;
                $$ LANGUAGE plpgsql;

                CREATE TRIGGER trg_punches_immutable
                    BEFORE UPDATE OR DELETE ON att.punches
                    FOR EACH ROW EXECUTE FUNCTION att.punches_immutable();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS trg_punches_immutable ON att.punches; DROP FUNCTION IF EXISTS att.punches_immutable();");
            migrationBuilder.Sql("ALTER TABLE att.device_user_map DROP CONSTRAINT IF EXISTS ex_device_user_map_no_overlap;");
            migrationBuilder.Sql("DROP INDEX IF EXISTS att.ix_punches_punched_at_brin;");

            migrationBuilder.DropTable(
                name: "device_user_map",
                schema: "att");

            migrationBuilder.DropTable(
                name: "punch_voids",
                schema: "att");

            migrationBuilder.DropTable(
                name: "punches",
                schema: "att");

            migrationBuilder.DropTable(
                name: "ingest_batches",
                schema: "att");

            migrationBuilder.DropTable(
                name: "devices",
                schema: "att");
        }
    }
}
