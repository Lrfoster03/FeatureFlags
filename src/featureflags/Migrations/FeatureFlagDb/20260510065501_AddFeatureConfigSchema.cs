using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FeatureFlags.Migrations.FeatureFlagDb
{
    /// <inheritdoc />
    public partial class AddFeatureConfigSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Schema",
                table: "Configs",
                type: "TEXT",
                nullable: false,
                defaultValue: "{}");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Schema",
                table: "Configs");
        }
    }
}
