using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace RegistraceOvcina.Web.Migrations
{
    /// <inheritdoc />
    public partial class AddFeedbackResponses : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "FeedbackInvitedAtUtc",
                table: "Registrations",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "FeedbackReminderLastSentAtUtc",
                table: "Registrations",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "FeedbackToken",
                table: "Registrations",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FeedbackAdultQuestionsJson",
                table: "Games",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "FeedbackClosesAtUtc",
                table: "Games",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FeedbackKidQuestionsJson",
                table: "Games",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "FeedbackOpensAtUtc",
                table: "Games",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "FeedbackResponses",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    RegistrationId = table.Column<int>(type: "integer", nullable: false),
                    AnswersJson = table.Column<string>(type: "text", nullable: false),
                    LastEditedBy = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    LastEditedByPersonId = table.Column<int>(type: "integer", nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    SubmittedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    PinToChronicle = table.Column<bool>(type: "boolean", nullable: false),
                    OrganizerNote = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FeedbackResponses", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FeedbackResponses_People_LastEditedByPersonId",
                        column: x => x.LastEditedByPersonId,
                        principalTable: "People",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_FeedbackResponses_Registrations_RegistrationId",
                        column: x => x.RegistrationId,
                        principalTable: "Registrations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Registrations_FeedbackToken",
                table: "Registrations",
                column: "FeedbackToken",
                unique: true,
                filter: "\"FeedbackToken\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_FeedbackResponses_LastEditedByPersonId",
                table: "FeedbackResponses",
                column: "LastEditedByPersonId");

            migrationBuilder.CreateIndex(
                name: "IX_FeedbackResponses_RegistrationId",
                table: "FeedbackResponses",
                column: "RegistrationId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "FeedbackResponses");

            migrationBuilder.DropIndex(
                name: "IX_Registrations_FeedbackToken",
                table: "Registrations");

            migrationBuilder.DropColumn(
                name: "FeedbackInvitedAtUtc",
                table: "Registrations");

            migrationBuilder.DropColumn(
                name: "FeedbackReminderLastSentAtUtc",
                table: "Registrations");

            migrationBuilder.DropColumn(
                name: "FeedbackToken",
                table: "Registrations");

            migrationBuilder.DropColumn(
                name: "FeedbackAdultQuestionsJson",
                table: "Games");

            migrationBuilder.DropColumn(
                name: "FeedbackClosesAtUtc",
                table: "Games");

            migrationBuilder.DropColumn(
                name: "FeedbackKidQuestionsJson",
                table: "Games");

            migrationBuilder.DropColumn(
                name: "FeedbackOpensAtUtc",
                table: "Games");
        }
    }
}
