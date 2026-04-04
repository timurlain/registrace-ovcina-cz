using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RegistraceOvcina.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddRoleSystemCharacterNameLodging : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "AdultRoles",
                table: "Registrations",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "AttendeeType",
                table: "Registrations",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "CharacterName",
                table: "Registrations",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "LodgingPreference",
                table: "Registrations",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PlayerSubType",
                table: "Registrations",
                type: "integer",
                nullable: true);

            // Migrate old Role values to new AttendeeType + AdultRoles
            migrationBuilder.Sql("""
                UPDATE "Registrations" SET "AttendeeType" = 0 WHERE "Role" = 0;
                UPDATE "Registrations" SET "AttendeeType" = 1 WHERE "Role" IN (1, 2, 3);
                UPDATE "Registrations" SET "AdultRoles" = 1 WHERE "Role" = 2;
                UPDATE "Registrations" SET "AdultRoles" = 2 WHERE "Role" = 1;
                UPDATE "Registrations" SET "AdultRoles" = 4 WHERE "Role" = 3;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AdultRoles",
                table: "Registrations");

            migrationBuilder.DropColumn(
                name: "AttendeeType",
                table: "Registrations");

            migrationBuilder.DropColumn(
                name: "CharacterName",
                table: "Registrations");

            migrationBuilder.DropColumn(
                name: "LodgingPreference",
                table: "Registrations");

            migrationBuilder.DropColumn(
                name: "PlayerSubType",
                table: "Registrations");
        }
    }
}
