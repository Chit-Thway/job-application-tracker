using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JobTracker.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddAutofillProfiles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AutofillProfiles",
                columns: table => new
                {
                    OwnerId = table.Column<string>(type: "text", nullable: false),
                    ProtectedProfile = table.Column<string>(type: "text", nullable: false),
                    Version = table.Column<Guid>(type: "uuid", nullable: false),
                    PairingHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    PairingChallenge = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    PairingExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    AccessHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    AccessExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    SecurityStamp = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AutofillProfiles", x => x.OwnerId);
                    table.ForeignKey(
                        name: "FK_AutofillProfiles_AspNetUsers_OwnerId",
                        column: x => x.OwnerId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AutofillProfiles_AccessHash",
                table: "AutofillProfiles",
                column: "AccessHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AutofillProfiles_PairingHash",
                table: "AutofillProfiles",
                column: "PairingHash",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AutofillProfiles");
        }
    }
}
