using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace violation_management_api.Migrations
{
    /// <inheritdoc />
    /// <remarks>
    /// Adds the vision-service update-lifecycle columns:
    ///  - TrackId    : tracker id of the detected person/object, so "Update"
    ///                 events can find the originally created violation.
    ///  - LastSeenAt : refreshed by PATCH /api/violations/internal/{id} while
    ///                 the violation is still being observed.
    /// Plus a covering index for the internal active-violation lookup
    /// (CameraId, TrackId, Timestamp).
    ///
    /// NOTE: this migration was hand-written (dotnet-ef was unavailable in the
    /// authoring environment) following the exact structure of the existing
    /// migrations in this folder.
    /// </remarks>
    public partial class AddViolationTrackLifecycle : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "LastSeenAt",
                table: "Violations",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "TrackId",
                table: "Violations",
                type: "bigint",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Violations_CameraId_TrackId_Timestamp",
                table: "Violations",
                columns: new[] { "CameraId", "TrackId", "Timestamp" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Violations_CameraId_TrackId_Timestamp",
                table: "Violations");

            migrationBuilder.DropColumn(
                name: "LastSeenAt",
                table: "Violations");

            migrationBuilder.DropColumn(
                name: "TrackId",
                table: "Violations");
        }
    }
}
