using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JobTracker.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddAccountTiers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "AccountTier",
                table: "AspNetUsers",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.Sql(
                """
                UPDATE "AspNetUsers" AS users
                SET "AccountTier" = 2
                WHERE EXISTS (
                    SELECT 1
                    FROM "AspNetUserRoles" AS user_roles
                    INNER JOIN "AspNetRoles" AS roles
                        ON roles."Id" = user_roles."RoleId"
                    WHERE user_roles."UserId" = users."Id"
                      AND roles."Name" = 'Admin'
                );
                """);

            migrationBuilder.AddCheckConstraint(
                name: "CK_AspNetUsers_AccountTier",
                table: "AspNetUsers",
                sql: "\"AccountTier\" IN (1, 2)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_AspNetUsers_AccountTier",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "AccountTier",
                table: "AspNetUsers");
        }
    }
}
