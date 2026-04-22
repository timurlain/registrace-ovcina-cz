using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RegistraceOvcina.Web.Migrations
{
    /// <inheritdoc />
    public partial class DropOrganizationalRoleCategory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // v0.9.26 — retire the "Organizational" (Category = 'staff') role concept.
            // The feature was unused per product owner; drop any lingering rows so the
            // Category column effectively only holds 'system' or 'game' going forward.
            //
            // 1) Unlink any user->role rows pointing at staff roles.
            // 2) Hard-delete the staff role rows themselves.
            //
            // The "system" and "game" rows are untouched. Schema (AspNetRoles.Category nvarchar)
            // is intentionally kept — default value on new rows is "game" via the admin UI
            // and "system" via DatabaseInitializer seeding.
            migrationBuilder.Sql(@"
                DELETE FROM ""AspNetUserRoles""
                WHERE ""RoleId"" IN (
                    SELECT ""Id"" FROM ""AspNetRoles""
                    WHERE ""Category"" NOT IN ('system', 'game')
                );
            ");

            migrationBuilder.Sql(@"
                DELETE FROM ""AspNetRoleClaims""
                WHERE ""RoleId"" IN (
                    SELECT ""Id"" FROM ""AspNetRoles""
                    WHERE ""Category"" NOT IN ('system', 'game')
                );
            ");

            migrationBuilder.Sql(@"
                DELETE FROM ""AspNetRoles""
                WHERE ""Category"" NOT IN ('system', 'game');
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // No rollback: we cannot resurrect the deleted 'staff' roles or their memberships.
            // The old DatabaseInitializer seed is also removed in v0.9.26, so running it
            // after this migration will not recreate them either.
        }
    }
}
