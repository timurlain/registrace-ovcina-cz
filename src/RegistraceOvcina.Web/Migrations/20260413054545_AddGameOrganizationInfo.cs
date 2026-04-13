using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RegistraceOvcina.Web.Migrations
{
    /// <inheritdoc />
    public partial class AddGameOrganizationInfo : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "OrganizationInfo",
                table: "Games",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "OrganizationInfo",
                table: "Games");
        }
    }
}
