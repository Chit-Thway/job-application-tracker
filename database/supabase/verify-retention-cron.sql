-- Read-only operational checks for the Supabase SQL editor.
-- These queries expose scheduler state and privacy-safe run records only.

select jobid, jobname, schedule, active
from cron.job
where jobname = 'job-application-tracker-retention';

select status, start_time, end_time, return_message
from cron.job_run_details
where jobid in (
    select jobid
    from cron.job
    where jobname = 'job-application-tracker-retention'
)
order by start_time desc
limit 10;

select "StartedAt", "CompletedAt", "Succeeded", "ScheduledCount", "DeletedCount", "ErrorCode"
from public."RetentionRuns"
order by "StartedAt" desc
limit 10;
