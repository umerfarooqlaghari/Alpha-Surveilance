using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace violation_management_api.Migrations
{
    /// <inheritdoc />
    public partial class ScopeCameraIdUniquenessPerTenant : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Cameras_CameraId",
                table: "Cameras");

            migrationBuilder.DropIndex(
                name: "IX_Cameras_TenantId",
                table: "Cameras");

            migrationBuilder.CreateIndex(
                name: "IX_Cameras_TenantId_CameraId",
                table: "Cameras",
                columns: new[] { "TenantId", "CameraId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Cameras_TenantId_CameraId",
                table: "Cameras");

            migrationBuilder.CreateIndex(
                name: "IX_Cameras_CameraId",
                table: "Cameras",
                column: "CameraId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Cameras_TenantId",
                table: "Cameras",
                column: "TenantId");
        }
    }
}
