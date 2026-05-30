using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace violation_management_api.Migrations
{
    /// <inheritdoc />
    public partial class EnforceEdgeDeviceSoftDeleteAndTenantCameraFk : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Cameras_EdgeDevices_DeviceId",
                table: "Cameras");

            migrationBuilder.AddUniqueConstraint(
                name: "AK_EdgeDevices_Id_TenantId",
                table: "EdgeDevices",
                columns: new[] { "Id", "TenantId" });

            migrationBuilder.CreateIndex(
                name: "IX_Cameras_DeviceId_TenantId",
                table: "Cameras",
                columns: new[] { "DeviceId", "TenantId" });

            migrationBuilder.AddForeignKey(
                name: "FK_Cameras_EdgeDevices_DeviceId_TenantId",
                table: "Cameras",
                columns: new[] { "DeviceId", "TenantId" },
                principalTable: "EdgeDevices",
                principalColumns: new[] { "Id", "TenantId" },
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Cameras_EdgeDevices_DeviceId_TenantId",
                table: "Cameras");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_EdgeDevices_Id_TenantId",
                table: "EdgeDevices");

            migrationBuilder.DropIndex(
                name: "IX_Cameras_DeviceId_TenantId",
                table: "Cameras");

            migrationBuilder.AddForeignKey(
                name: "FK_Cameras_EdgeDevices_DeviceId",
                table: "Cameras",
                column: "DeviceId",
                principalTable: "EdgeDevices",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }
    }
}
