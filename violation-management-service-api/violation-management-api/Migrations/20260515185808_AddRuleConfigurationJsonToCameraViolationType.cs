using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace violation_management_api.Migrations
{
    /// <inheritdoc />
    public partial class AddRuleConfigurationJsonToCameraViolationType : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                ALTER TABLE ""CameraViolationTypes""
                ADD COLUMN IF NOT EXISTS ""RuleConfigurationJson"" text;
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                ALTER TABLE ""CameraViolationTypes""
                DROP COLUMN IF EXISTS ""RuleConfigurationJson"";
            ");
        }
    }
}
