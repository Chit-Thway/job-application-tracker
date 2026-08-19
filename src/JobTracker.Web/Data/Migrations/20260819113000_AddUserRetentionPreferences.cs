using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JobTracker.Web.Data.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20260819113000_AddUserRetentionPreferences")]
public sealed class AddUserRetentionPreferences : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<int>(
            name: "DeletionGraceDays",
            table: "AspNetUsers",
            type: "integer",
            nullable: false,
            defaultValue: 14);

        migrationBuilder.AddColumn<int>(
            name: "RetentionMonths",
            table: "AspNetUsers",
            type: "integer",
            nullable: false,
            defaultValue: 3);

        migrationBuilder.AddCheckConstraint(
            name: "CK_AspNetUsers_DeletionGraceDays",
            table: "AspNetUsers",
            sql: "\"DeletionGraceDays\" IN (3, 5, 10, 14)");

        migrationBuilder.AddCheckConstraint(
            name: "CK_AspNetUsers_RetentionMonths",
            table: "AspNetUsers",
            sql: "\"RetentionMonths\" IN (1, 2, 3)");

        migrationBuilder.Sql(UserConfiguredRetentionFunction);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(DefaultRetentionFunction);

        migrationBuilder.DropCheckConstraint(
            name: "CK_AspNetUsers_DeletionGraceDays",
            table: "AspNetUsers");

        migrationBuilder.DropCheckConstraint(
            name: "CK_AspNetUsers_RetentionMonths",
            table: "AspNetUsers");

        migrationBuilder.DropColumn(
            name: "DeletionGraceDays",
            table: "AspNetUsers");

        migrationBuilder.DropColumn(
            name: "RetentionMonths",
            table: "AspNetUsers");
    }

    private const string UserConfiguredRetentionFunction =
        """
        CREATE OR REPLACE FUNCTION public.process_job_application_retention(
            p_now timestamp with time zone DEFAULT now())
        RETURNS TABLE (
            run_id bigint,
            succeeded boolean,
            scheduled_count integer,
            deleted_count integer,
            error_code text)
        LANGUAGE plpgsql
        SECURITY INVOKER
        SET search_path = pg_catalog, public
        AS $function$
        DECLARE
            v_started_at timestamp with time zone := clock_timestamp();
            v_completed_at timestamp with time zone;
            v_scheduled_count integer := 0;
            v_deleted_count integer := 0;
            v_run_id bigint;
            v_error_code text;
        BEGIN
            DELETE FROM public."RetentionRuns"
            WHERE "CompletedAt" < p_now - interval '90 days';

            UPDATE public."JobApplications"
            SET "DeletionScheduledAt" = NULL
            WHERE "IsSavedForever" = TRUE
              AND "DeletionScheduledAt" IS NOT NULL;

            WITH scheduled AS (
                UPDATE public."JobApplications" AS application
                SET "DeletionScheduledAt" =
                    p_now + owner."DeletionGraceDays" * interval '1 day'
                FROM public."AspNetUsers" AS owner
                WHERE owner."Id" = application."OwnerId"
                  AND application."IsSavedForever" = FALSE
                  AND application."DeletionScheduledAt" IS NULL
                  AND (p_now AT TIME ZONE owner."TimeZoneId")::date
                      >= (application."AppliedOn"
                          + owner."RetentionMonths" * interval '1 month')::date
                RETURNING application."Id"
            )
            SELECT count(*)::integer
            INTO v_scheduled_count
            FROM scheduled;

            WITH deleted AS (
                DELETE FROM public."JobApplications"
                WHERE "IsSavedForever" = FALSE
                  AND "DeletionScheduledAt" IS NOT NULL
                  AND "DeletionScheduledAt" <= p_now
                RETURNING "Id"
            )
            SELECT count(*)::integer
            INTO v_deleted_count
            FROM deleted;

            v_completed_at := clock_timestamp();
            INSERT INTO public."RetentionRuns" (
                "StartedAt",
                "CompletedAt",
                "Succeeded",
                "ScheduledCount",
                "DeletedCount",
                "ErrorCode")
            VALUES (
                v_started_at,
                v_completed_at,
                TRUE,
                v_scheduled_count,
                v_deleted_count,
                NULL)
            RETURNING "Id" INTO v_run_id;

            RETURN QUERY
            SELECT v_run_id, TRUE, v_scheduled_count, v_deleted_count, NULL::text;
        EXCEPTION WHEN OTHERS THEN
            GET STACKED DIAGNOSTICS v_error_code = RETURNED_SQLSTATE;
            v_completed_at := clock_timestamp();
            INSERT INTO public."RetentionRuns" (
                "StartedAt",
                "CompletedAt",
                "Succeeded",
                "ScheduledCount",
                "DeletedCount",
                "ErrorCode")
            VALUES (
                v_started_at,
                v_completed_at,
                FALSE,
                0,
                0,
                left(v_error_code, 10))
            RETURNING "Id" INTO v_run_id;

            RETURN QUERY
            SELECT v_run_id, FALSE, 0, 0, left(v_error_code, 10);
        END;
        $function$;

        REVOKE ALL ON FUNCTION public.process_job_application_retention(timestamp with time zone)
            FROM PUBLIC;
        GRANT EXECUTE ON FUNCTION public.process_job_application_retention(timestamp with time zone)
            TO postgres;
        """;

    private const string DefaultRetentionFunction =
        """
        CREATE OR REPLACE FUNCTION public.process_job_application_retention(
            p_now timestamp with time zone DEFAULT now())
        RETURNS TABLE (
            run_id bigint,
            succeeded boolean,
            scheduled_count integer,
            deleted_count integer,
            error_code text)
        LANGUAGE plpgsql
        SECURITY INVOKER
        SET search_path = pg_catalog, public
        AS $function$
        DECLARE
            v_started_at timestamp with time zone := clock_timestamp();
            v_completed_at timestamp with time zone;
            v_scheduled_count integer := 0;
            v_deleted_count integer := 0;
            v_run_id bigint;
            v_error_code text;
        BEGIN
            DELETE FROM public."RetentionRuns"
            WHERE "CompletedAt" < p_now - interval '90 days';

            UPDATE public."JobApplications"
            SET "DeletionScheduledAt" = NULL
            WHERE "IsSavedForever" = TRUE
              AND "DeletionScheduledAt" IS NOT NULL;

            WITH scheduled AS (
                UPDATE public."JobApplications" AS application
                SET "DeletionScheduledAt" = p_now + interval '14 days'
                FROM public."AspNetUsers" AS owner
                WHERE owner."Id" = application."OwnerId"
                  AND application."IsSavedForever" = FALSE
                  AND application."DeletionScheduledAt" IS NULL
                  AND (p_now AT TIME ZONE owner."TimeZoneId")::date
                      >= (application."AppliedOn" + interval '3 months')::date
                RETURNING application."Id"
            )
            SELECT count(*)::integer
            INTO v_scheduled_count
            FROM scheduled;

            WITH deleted AS (
                DELETE FROM public."JobApplications"
                WHERE "IsSavedForever" = FALSE
                  AND "DeletionScheduledAt" IS NOT NULL
                  AND "DeletionScheduledAt" <= p_now
                RETURNING "Id"
            )
            SELECT count(*)::integer
            INTO v_deleted_count
            FROM deleted;

            v_completed_at := clock_timestamp();
            INSERT INTO public."RetentionRuns" (
                "StartedAt", "CompletedAt", "Succeeded", "ScheduledCount", "DeletedCount", "ErrorCode")
            VALUES (
                v_started_at, v_completed_at, TRUE, v_scheduled_count, v_deleted_count, NULL)
            RETURNING "Id" INTO v_run_id;

            RETURN QUERY
            SELECT v_run_id, TRUE, v_scheduled_count, v_deleted_count, NULL::text;
        EXCEPTION WHEN OTHERS THEN
            GET STACKED DIAGNOSTICS v_error_code = RETURNED_SQLSTATE;
            v_completed_at := clock_timestamp();
            INSERT INTO public."RetentionRuns" (
                "StartedAt", "CompletedAt", "Succeeded", "ScheduledCount", "DeletedCount", "ErrorCode")
            VALUES (
                v_started_at, v_completed_at, FALSE, 0, 0, left(v_error_code, 10))
            RETURNING "Id" INTO v_run_id;

            RETURN QUERY
            SELECT v_run_id, FALSE, 0, 0, left(v_error_code, 10);
        END;
        $function$;

        REVOKE ALL ON FUNCTION public.process_job_application_retention(timestamp with time zone)
            FROM PUBLIC;
        GRANT EXECUTE ON FUNCTION public.process_job_application_retention(timestamp with time zone)
            TO postgres;
        """;
}
