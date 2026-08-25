using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JobTracker.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddApplicationPortalUrl : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ApplicationPortalUrl",
                table: "JobApplications",
                type: "character varying(2048)",
                maxLength: 2048,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ApplicationPortalUrl",
                table: "JobApplications");
        }
    }
}
