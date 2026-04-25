using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FeatureFlags.TempMigrations
{
    /// <inheritdoc />
    public partial class FixSalt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Salt",
                table: "FeatureFlags",
                type: "TEXT",
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Salt",
                table: "FeatureFlags");
        }
    }
}
