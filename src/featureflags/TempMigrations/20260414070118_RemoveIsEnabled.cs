using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FeatureFlags.TempMigrations
{
    /// <inheritdoc />
    public partial class RemoveIsEnabled : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsEnabled",
                table: "FeatureFlags");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsEnabled",
                table: "FeatureFlags",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);
        }
    }
}
