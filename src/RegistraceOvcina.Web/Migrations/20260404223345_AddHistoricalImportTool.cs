using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace RegistraceOvcina.Web.Migrations
{
    /// <inheritdoc />
    public partial class AddHistoricalImportTool : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "HistoricalImportBatches",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Label = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    SourceFormat = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    SourceFileName = table.Column<string>(type: "character varying(260)", maxLength: 260, nullable: false),
                    GameId = table.Column<int>(type: "integer", nullable: false),
                    ImportedByUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                    ImportedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    TotalSourceRows = table.Column<int>(type: "integer", nullable: false),
                    HouseholdCount = table.Column<int>(type: "integer", nullable: false),
                    RegistrationCount = table.Column<int>(type: "integer", nullable: false),
                    PersonCreatedCount = table.Column<int>(type: "integer", nullable: false),
                    PersonMatchedCount = table.Column<int>(type: "integer", nullable: false),
                    CharacterCreatedCount = table.Column<int>(type: "integer", nullable: false),
                    WarningCount = table.Column<int>(type: "integer", nullable: false),
                    NotesJson = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HistoricalImportBatches", x => x.Id);
                    table.ForeignKey(
                        name: "FK_HistoricalImportBatches_AspNetUsers_ImportedByUserId",
                        column: x => x.ImportedByUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_HistoricalImportBatches_Games_GameId",
                        column: x => x.GameId,
                        principalTable: "Games",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "HistoricalImportRows",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    LastBatchId = table.Column<int>(type: "integer", nullable: true),
                    SourceFormat = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    SourceSheet = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    SourceKey = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    SourceLabel = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    LinkedPersonId = table.Column<int>(type: "integer", nullable: true),
                    LinkedSubmissionId = table.Column<int>(type: "integer", nullable: true),
                    LinkedRegistrationId = table.Column<int>(type: "integer", nullable: true),
                    LinkedCharacterId = table.Column<int>(type: "integer", nullable: true),
                    WarningMessage = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    FirstImportedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LastImportedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HistoricalImportRows", x => x.Id);
                    table.ForeignKey(
                        name: "FK_HistoricalImportRows_Characters_LinkedCharacterId",
                        column: x => x.LinkedCharacterId,
                        principalTable: "Characters",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_HistoricalImportRows_HistoricalImportBatches_LastBatchId",
                        column: x => x.LastBatchId,
                        principalTable: "HistoricalImportBatches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_HistoricalImportRows_People_LinkedPersonId",
                        column: x => x.LinkedPersonId,
                        principalTable: "People",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_HistoricalImportRows_RegistrationSubmissions_LinkedSubmissi~",
                        column: x => x.LinkedSubmissionId,
                        principalTable: "RegistrationSubmissions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_HistoricalImportRows_Registrations_LinkedRegistrationId",
                        column: x => x.LinkedRegistrationId,
                        principalTable: "Registrations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_HistoricalImportBatches_GameId",
                table: "HistoricalImportBatches",
                column: "GameId");

            migrationBuilder.CreateIndex(
                name: "IX_HistoricalImportBatches_ImportedByUserId",
                table: "HistoricalImportBatches",
                column: "ImportedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_HistoricalImportRows_LastBatchId",
                table: "HistoricalImportRows",
                column: "LastBatchId");

            migrationBuilder.CreateIndex(
                name: "IX_HistoricalImportRows_LinkedCharacterId",
                table: "HistoricalImportRows",
                column: "LinkedCharacterId");

            migrationBuilder.CreateIndex(
                name: "IX_HistoricalImportRows_LinkedPersonId",
                table: "HistoricalImportRows",
                column: "LinkedPersonId");

            migrationBuilder.CreateIndex(
                name: "IX_HistoricalImportRows_LinkedRegistrationId",
                table: "HistoricalImportRows",
                column: "LinkedRegistrationId");

            migrationBuilder.CreateIndex(
                name: "IX_HistoricalImportRows_LinkedSubmissionId",
                table: "HistoricalImportRows",
                column: "LinkedSubmissionId");

            migrationBuilder.CreateIndex(
                name: "IX_HistoricalImportRows_SourceFormat_SourceSheet_SourceKey",
                table: "HistoricalImportRows",
                columns: new[] { "SourceFormat", "SourceSheet", "SourceKey" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "HistoricalImportRows");

            migrationBuilder.DropTable(
                name: "HistoricalImportBatches");
        }
    }
}
