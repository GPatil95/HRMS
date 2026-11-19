using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hrms.Infrastructure.Persistence.Migrations.Tenant
{
    /// <inheritdoc />
    public partial class AttendanceDefaultIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_work_calendars_is_default",
                schema: "att",
                table: "work_calendars");

            migrationBuilder.CreateIndex(
                name: "ix_work_calendars_is_default",
                schema: "att",
                table: "work_calendars",
                column: "is_default",
                unique: true,
                filter: "is_default");

            migrationBuilder.CreateIndex(
                name: "ix_attendance_policies_is_default",
                schema: "att",
                table: "attendance_policies",
                column: "is_default",
                unique: true,
                filter: "is_default");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_work_calendars_is_default",
                schema: "att",
                table: "work_calendars");

            migrationBuilder.DropIndex(
                name: "ix_attendance_policies_is_default",
                schema: "att",
                table: "attendance_policies");

            migrationBuilder.CreateIndex(
                name: "ix_work_calendars_is_default",
                schema: "att",
                table: "work_calendars",
                column: "is_default",
                filter: "is_default");
        }
    }
}
