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
            // Role is stored as varchar (EF string conversion), not integer
            migrationBuilder.Sql("""
                UPDATE "Registrations" SET "AttendeeType" = 0 WHERE "Role" = 'Player';
                UPDATE "Registrations" SET "AttendeeType" = 1 WHERE "Role" IN ('Npc', 'Monster', 'TechSupport');
                UPDATE "Registrations" SET "AdultRoles" = 1 WHERE "Role" = 'Monster';
                UPDATE "Registrations" SET "AdultRoles" = 2 WHERE "Role" = 'Npc';
                UPDATE "Registrations" SET "AdultRoles" = 4 WHERE "Role" = 'TechSupport';
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
