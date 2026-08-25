using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JobTracker.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddExtensionCaptureHandoffs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ExtensionCaptureHandoffs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TokenHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ProtectedPayload = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ConcurrencyStamp = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExtensionCaptureHandoffs", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ExtensionCaptureHandoffs_ExpiresAt",
                table: "ExtensionCaptureHandoffs",
                column: "ExpiresAt");

            migrationBuilder.CreateIndex(
                name: "IX_ExtensionCaptureHandoffs_TokenHash",
                table: "ExtensionCaptureHandoffs",
                column: "TokenHash",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ExtensionCaptureHandoffs");
        }
    }
}
