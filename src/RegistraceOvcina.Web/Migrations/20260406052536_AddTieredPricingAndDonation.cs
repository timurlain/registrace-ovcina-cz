using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RegistraceOvcina.Web.Migrations
{
    /// <inheritdoc />
    public partial class AddTieredPricingAndDonation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "VoluntaryDonation",
                table: "RegistrationSubmissions",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "SecondChildPrice",
                table: "Games",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "ThirdPlusChildPrice",
                table: "Games",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "VoluntaryDonation",
                table: "RegistrationSubmissions");

            migrationBuilder.DropColumn(
                name: "SecondChildPrice",
                table: "Games");

            migrationBuilder.DropColumn(
                name: "ThirdPlusChildPrice",
                table: "Games");
        }
    }
}
