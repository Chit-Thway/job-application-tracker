-- Run once in the Supabase SQL editor after the Milestone 8 migration is applied.
-- The job runs hourly. The database function is atomic and safe to call repeatedly.

create extension if not exists pg_cron;

select cron.unschedule(jobid)
from cron.job
where jobname = 'job-application-tracker-retention';

select cron.schedule(
    'job-application-tracker-retention',
    '17 * * * *',
    $retention$select * from public.process_job_application_retention();$retention$
);

select jobid, jobname, schedule, command, active
from cron.job
where jobname = 'job-application-tracker-retention';
