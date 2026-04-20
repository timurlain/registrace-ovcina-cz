using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace RegistraceOvcina.Web.Migrations
{
    /// <inheritdoc />
    public partial class AddCharacterPrepAndStartingEquipment : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "CharacterPrepInvitedAtUtc",
                table: "RegistrationSubmissions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "CharacterPrepReminderLastSentAtUtc",
                table: "RegistrationSubmissions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CharacterPrepToken",
                table: "RegistrationSubmissions",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CharacterPrepNote",
                table: "Registrations",
                type: "character varying(4000)",
                maxLength: 4000,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "CharacterPrepUpdatedAtUtc",
                table: "Registrations",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "StartingEquipmentOptionId",
                table: "Registrations",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "StartingEquipmentOptions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    GameId = table.Column<int>(type: "integer", nullable: false),
                    Key = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    DisplayName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    SortOrder = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StartingEquipmentOptions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StartingEquipmentOptions_Games_GameId",
                        column: x => x.GameId,
                        principalTable: "Games",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RegistrationSubmissions_CharacterPrepToken",
                table: "RegistrationSubmissions",
                column: "CharacterPrepToken",
                unique: true,
                filter: "\"CharacterPrepToken\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Registrations_StartingEquipmentOptionId",
                table: "Registrations",
                column: "StartingEquipmentOptionId");

            migrationBuilder.CreateIndex(
                name: "IX_StartingEquipmentOptions_GameId_Key",
                table: "StartingEquipmentOptions",
                columns: new[] { "GameId", "Key" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Registrations_StartingEquipmentOptions_StartingEquipmentOpt~",
                table: "Registrations",
                column: "StartingEquipmentOptionId",
                principalTable: "StartingEquipmentOptions",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Registrations_StartingEquipmentOptions_StartingEquipmentOpt~",
                table: "Registrations");

            migrationBuilder.DropTable(
                name: "StartingEquipmentOptions");

            migrationBuilder.DropIndex(
                name: "IX_RegistrationSubmissions_CharacterPrepToken",
                table: "RegistrationSubmissions");

            migrationBuilder.DropIndex(
                name: "IX_Registrations_StartingEquipmentOptionId",
                table: "Registrations");

            migrationBuilder.DropColumn(
                name: "CharacterPrepInvitedAtUtc",
                table: "RegistrationSubmissions");

            migrationBuilder.DropColumn(
                name: "CharacterPrepReminderLastSentAtUtc",
                table: "RegistrationSubmissions");

            migrationBuilder.DropColumn(
                name: "CharacterPrepToken",
                table: "RegistrationSubmissions");

            migrationBuilder.DropColumn(
                name: "CharacterPrepNote",
                table: "Registrations");

            migrationBuilder.DropColumn(
                name: "CharacterPrepUpdatedAtUtc",
                table: "Registrations");

            migrationBuilder.DropColumn(
                name: "StartingEquipmentOptionId",
                table: "Registrations");
        }
    }
}
