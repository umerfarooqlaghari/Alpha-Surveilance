using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Pgvector;

#nullable disable

namespace violation_management_api.Migrations
{
    /// <inheritdoc />
    public partial class AddWorkerProfilesAndPgvector : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Ensure pgvector extension is installed before any vector DDL.
            // AlterDatabase().Annotation(...) is not guaranteed to run CREATE EXTENSION
            // before table/index creation on managed Postgres (e.g. Render).
            // NOTE: CREATE EXTENSION cannot run inside a transaction block on PostgreSQL,
            // so suppressTransaction: true is required here.
            migrationBuilder.Sql("CREATE EXTENSION IF NOT EXISTS vector;", suppressTransaction: true);

            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:PostgresExtension:vector", ",,");

            // Use raw SQL with IF NOT EXISTS to be idempotent in case a previous
            // failed migration run already added this column.
            migrationBuilder.Sql(
                "ALTER TABLE \"EdgeDevices\" ADD COLUMN IF NOT EXISTS \"DeviceKey\" character varying(255) NULL;");

            // Drop partial table if it was left behind by a previous failed run
            // (the vector column cannot be created without the extension active).
            migrationBuilder.Sql("DROP TABLE IF EXISTS worker_profiles;");

            migrationBuilder.CreateTable(
                name: "worker_profiles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    PersonTag = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Embedding = table.Column<Vector>(type: "vector(512)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_worker_profiles", x => x.Id);
                    table.ForeignKey(
                        name: "FK_worker_profiles_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_worker_profiles_TenantId",
                table: "worker_profiles",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_worker_profiles_TenantId_PersonTag",
                table: "worker_profiles",
                columns: new[] { "TenantId", "PersonTag" });

            migrationBuilder.Sql(
                "CREATE INDEX IF NOT EXISTS idx_worker_profiles_tenant_embedding ON worker_profiles USING hnsw (\"Embedding\" vector_cosine_ops);",
                suppressTransaction: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "worker_profiles");

            migrationBuilder.DropColumn(
                name: "DeviceKey",
                table: "EdgeDevices");

            migrationBuilder.AlterDatabase()
                .OldAnnotation("Npgsql:PostgresExtension:vector", ",,");
        }
    }
}
