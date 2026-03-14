using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace violation_management_api.Migrations
{
    /// <inheritdoc />
    public partial class AddCameraWebRtcFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsStreaming",
                table: "Cameras",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "WhepUrl",
                table: "Cameras",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "WhipUrl",
                table: "Cameras",
                type: "text",
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsStreaming",
                table: "Cameras");

            migrationBuilder.DropColumn(
                name: "WhepUrl",
                table: "Cameras");

            migrationBuilder.DropColumn(
                name: "WhipUrl",
                table: "Cameras");
        }
    }
}
