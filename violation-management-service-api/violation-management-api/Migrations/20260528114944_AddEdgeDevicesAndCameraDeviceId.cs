using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace violation_management_api.Migrations
{
    /// <inheritdoc />
    public partial class AddEdgeDevicesAndCameraDeviceId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "DeviceId",
                table: "Cameras",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "EdgeDevices",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    LocationId = table.Column<Guid>(type: "uuid", nullable: true),
                    DeviceIdentifier = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Hostname = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    DisplayName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Status = table.Column<string>(type: "text", nullable: false),
                    LastSeenAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    RegisteredAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EdgeDevices", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EdgeDevices_Locations_LocationId",
                        column: x => x.LocationId,
                        principalTable: "Locations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_EdgeDevices_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Cameras_DeviceId",
                table: "Cameras",
                column: "DeviceId");

            migrationBuilder.CreateIndex(
                name: "IX_EdgeDevices_LocationId",
                table: "EdgeDevices",
                column: "LocationId");

            migrationBuilder.CreateIndex(
                name: "IX_EdgeDevices_TenantId",
                table: "EdgeDevices",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_EdgeDevices_TenantId_DeviceIdentifier",
                table: "EdgeDevices",
                columns: new[] { "TenantId", "DeviceIdentifier" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Cameras_EdgeDevices_DeviceId",
                table: "Cameras",
                column: "DeviceId",
                principalTable: "EdgeDevices",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Cameras_EdgeDevices_DeviceId",
                table: "Cameras");

            migrationBuilder.DropTable(
                name: "EdgeDevices");

            migrationBuilder.DropIndex(
                name: "IX_Cameras_DeviceId",
                table: "Cameras");

            migrationBuilder.DropColumn(
                name: "DeviceId",
                table: "Cameras");
        }
    }
}
