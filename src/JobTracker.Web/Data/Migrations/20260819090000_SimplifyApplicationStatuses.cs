using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JobTracker.Web.Data.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20260819090000_SimplifyApplicationStatuses")]
public sealed class SimplifyApplicationStatuses : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            UPDATE "JobApplications"
            SET "Stage" = 'Screening'
            WHERE "Stage" = 'RecruiterContact';

            UPDATE "JobApplications"
            SET "Stage" = 'Interview'
            WHERE "Stage" = 'ReferenceCheck';

            UPDATE "JobApplications"
            SET "Outcome" = 'Withdrawn'
            WHERE "Outcome" = 'OfferDeclined';
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // The consolidated values cannot be split back into their former meanings reliably.
    }
}
