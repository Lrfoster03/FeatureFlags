using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FeatureFlags.Data.Migrations.FeatureFlags
{
    /// <inheritdoc />
    public partial class AddProjectMemberDisplayFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DisplayName",
                table: "ProjectMembers",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Email",
                table: "ProjectMembers",
                type: "TEXT",
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DisplayName",
                table: "ProjectMembers");

            migrationBuilder.DropColumn(
                name: "Email",
                table: "ProjectMembers");
        }
    }
}
