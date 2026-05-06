using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RegistraceOvcina.Web.Migrations
{
    /// <inheritdoc />
    public partial class AddFeedbackEmailTemplates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "FeedbackAdultIndividualHtmlTemplate",
                table: "Games",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FeedbackAdultIndividualSubjectTemplate",
                table: "Games",
                type: "character varying(400)",
                maxLength: 400,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FeedbackBundleHtmlTemplate",
                table: "Games",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FeedbackBundleSubjectTemplate",
                table: "Games",
                type: "character varying(400)",
                maxLength: 400,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "FeedbackAdultIndividualHtmlTemplate",
                table: "Games");

            migrationBuilder.DropColumn(
                name: "FeedbackAdultIndividualSubjectTemplate",
                table: "Games");

            migrationBuilder.DropColumn(
                name: "FeedbackBundleHtmlTemplate",
                table: "Games");

            migrationBuilder.DropColumn(
                name: "FeedbackBundleSubjectTemplate",
                table: "Games");
        }
    }
}
