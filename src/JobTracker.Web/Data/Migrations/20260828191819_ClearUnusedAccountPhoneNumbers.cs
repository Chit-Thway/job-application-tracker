using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JobTracker.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class ClearUnusedAccountPhoneNumbers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                UPDATE "AspNetUsers"
                SET "PhoneNumber" = NULL,
                    "PhoneNumberConfirmed" = FALSE
                WHERE "PhoneNumber" IS NOT NULL
                   OR "PhoneNumberConfirmed" = TRUE;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Cleared personal information cannot and should not be reconstructed.
        }
    }
}
