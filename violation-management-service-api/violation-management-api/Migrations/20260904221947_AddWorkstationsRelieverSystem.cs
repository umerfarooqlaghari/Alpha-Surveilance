using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace violation_management_api.Migrations
{
    /// <inheritdoc />
    public partial class AddWorkstationsRelieverSystem : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Violations_Cameras_CameraId1",
                table: "Violations");

            migrationBuilder.DropIndex(
                name: "IX_Violations_CameraId1",
                table: "Violations");

            migrationBuilder.DropColumn(
                name: "CameraId1",
                table: "Violations");

            migrationBuilder.CreateTable(
                name: "Workstations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    LocationId = table.Column<Guid>(type: "uuid", nullable: true),
                    CameraId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    Code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    PolygonJson = table.Column<string>(type: "jsonb", nullable: false),
                    RequiredPrimaryWorkers = table.Column<int>(type: "integer", nullable: false),
                    MaxRelievers = table.Column<int>(type: "integer", nullable: false),
                    HandoverThresholdSeconds = table.Column<int>(type: "integer", nullable: false),
                    MaxReliefDurationSeconds = table.Column<int>(type: "integer", nullable: false),
                    CurrentStatus = table.Column<int>(type: "integer", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Workstations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Workstations_Cameras_CameraId",
                        column: x => x.CameraId,
                        principalTable: "Cameras",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Workstations_Locations_LocationId",
                        column: x => x.LocationId,
                        principalTable: "Locations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_Workstations_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ReliefSessions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    LocationId = table.Column<Guid>(type: "uuid", nullable: true),
                    WorkstationId = table.Column<Guid>(type: "uuid", nullable: false),
                    CameraId = table.Column<Guid>(type: "uuid", nullable: false),
                    PrimaryEmployeeId = table.Column<Guid>(type: "uuid", nullable: true),
                    PrimaryEmployeeExternalId = table.Column<string>(type: "text", nullable: true),
                    RelieverEmployeeId = table.Column<Guid>(type: "uuid", nullable: true),
                    RelieverEmployeeExternalId = table.Column<string>(type: "text", nullable: true),
                    PrimaryLeftAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    RelieverArrivedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    HandoverLatencySeconds = table.Column<int>(type: "integer", nullable: true),
                    ReliefEndedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ReliefDurationSeconds = table.Column<int>(type: "integer", nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    ViolationId = table.Column<Guid>(type: "uuid", nullable: true),
                    Notes = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReliefSessions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ReliefSessions_Cameras_CameraId",
                        column: x => x.CameraId,
                        principalTable: "Cameras",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ReliefSessions_Employees_PrimaryEmployeeId",
                        column: x => x.PrimaryEmployeeId,
                        principalTable: "Employees",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_ReliefSessions_Employees_RelieverEmployeeId",
                        column: x => x.RelieverEmployeeId,
                        principalTable: "Employees",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_ReliefSessions_Violations_ViolationId",
                        column: x => x.ViolationId,
                        principalTable: "Violations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_ReliefSessions_Workstations_WorkstationId",
                        column: x => x.WorkstationId,
                        principalTable: "Workstations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "WorkstationDowntimeLogs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkstationId = table.Column<Guid>(type: "uuid", nullable: false),
                    StartTime = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    EndTime = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DurationSeconds = table.Column<int>(type: "integer", nullable: true),
                    Reason = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkstationDowntimeLogs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WorkstationDowntimeLogs_Workstations_WorkstationId",
                        column: x => x.WorkstationId,
                        principalTable: "Workstations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "WorkstationWorkerAssignments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkstationId = table.Column<Guid>(type: "uuid", nullable: false),
                    EmployeeId = table.Column<Guid>(type: "uuid", nullable: false),
                    Role = table.Column<int>(type: "integer", nullable: false),
                    ShiftStartTime = table.Column<TimeSpan>(type: "interval", nullable: true),
                    ShiftEndTime = table.Column<TimeSpan>(type: "interval", nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkstationWorkerAssignments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WorkstationWorkerAssignments_Employees_EmployeeId",
                        column: x => x.EmployeeId,
                        principalTable: "Employees",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_WorkstationWorkerAssignments_Workstations_WorkstationId",
                        column: x => x.WorkstationId,
                        principalTable: "Workstations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ReliefSessions_CameraId",
                table: "ReliefSessions",
                column: "CameraId");

            migrationBuilder.CreateIndex(
                name: "IX_ReliefSessions_PrimaryEmployeeId",
                table: "ReliefSessions",
                column: "PrimaryEmployeeId");

            migrationBuilder.CreateIndex(
                name: "IX_ReliefSessions_PrimaryLeftAt",
                table: "ReliefSessions",
                column: "PrimaryLeftAt");

            migrationBuilder.CreateIndex(
                name: "IX_ReliefSessions_RelieverEmployeeId",
                table: "ReliefSessions",
                column: "RelieverEmployeeId");

            migrationBuilder.CreateIndex(
                name: "IX_ReliefSessions_Status",
                table: "ReliefSessions",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_ReliefSessions_TenantId",
                table: "ReliefSessions",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_ReliefSessions_ViolationId",
                table: "ReliefSessions",
                column: "ViolationId");

            migrationBuilder.CreateIndex(
                name: "IX_ReliefSessions_WorkstationId",
                table: "ReliefSessions",
                column: "WorkstationId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkstationDowntimeLogs_StartTime",
                table: "WorkstationDowntimeLogs",
                column: "StartTime");

            migrationBuilder.CreateIndex(
                name: "IX_WorkstationDowntimeLogs_TenantId",
                table: "WorkstationDowntimeLogs",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkstationDowntimeLogs_WorkstationId",
                table: "WorkstationDowntimeLogs",
                column: "WorkstationId");

            migrationBuilder.CreateIndex(
                name: "IX_Workstations_CameraId",
                table: "Workstations",
                column: "CameraId");

            migrationBuilder.CreateIndex(
                name: "IX_Workstations_LocationId",
                table: "Workstations",
                column: "LocationId");

            migrationBuilder.CreateIndex(
                name: "IX_Workstations_TenantId",
                table: "Workstations",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_Workstations_TenantId_Code",
                table: "Workstations",
                columns: new[] { "TenantId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WorkstationWorkerAssignments_EmployeeId",
                table: "WorkstationWorkerAssignments",
                column: "EmployeeId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkstationWorkerAssignments_TenantId",
                table: "WorkstationWorkerAssignments",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkstationWorkerAssignments_WorkstationId",
                table: "WorkstationWorkerAssignments",
                column: "WorkstationId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkstationWorkerAssignments_WorkstationId_EmployeeId_Role",
                table: "WorkstationWorkerAssignments",
                columns: new[] { "WorkstationId", "EmployeeId", "Role" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ReliefSessions");

            migrationBuilder.DropTable(
                name: "WorkstationDowntimeLogs");

            migrationBuilder.DropTable(
                name: "WorkstationWorkerAssignments");

            migrationBuilder.DropTable(
                name: "Workstations");

            migrationBuilder.AddColumn<Guid>(
                name: "CameraId1",
                table: "Violations",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Violations_CameraId1",
                table: "Violations",
                column: "CameraId1");

            migrationBuilder.AddForeignKey(
                name: "FK_Violations_Cameras_CameraId1",
                table: "Violations",
                column: "CameraId1",
                principalTable: "Cameras",
                principalColumn: "Id");
        }
    }
}
