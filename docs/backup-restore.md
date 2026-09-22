# Supabase backup and restore rehearsal

Supabase recommends that Free-plan projects regularly create off-site logical exports with `supabase db dump`; paid-plan dashboard backups do not remove the need to rehearse restoration. Follow the current [Supabase database backup guidance](https://supabase.com/docs/guides/platform/backups) and [CLI backup/restore procedure](https://supabase.com/docs/guides/platform/migrating-within-supabase/backup-restore).

## Safety boundary

Restore only into a newly created, disposable non-production project. Never point the restore command at the source project, the production project, or a database containing data you need. Verify the source and target project references, hostnames, and database passwords twice. A restore is intentionally not automated by this repository because choosing the destructive target requires a human decision.

## Backup

1. Install the current Supabase CLI and PostgreSQL `psql` tooling. Create a private backup directory outside the Git repository.
2. Get the source Session Pooler connection string from **Connect**. Keep it in a session-scoped secret variable rather than a script or command history.
3. From the private backup directory, create the three logical exports recommended by Supabase:

   ```text
   supabase db dump --db-url <SOURCE_CONNECTION> -f roles.sql --role-only
   supabase db dump --db-url <SOURCE_CONNECTION> -f schema.sql
   supabase db dump --db-url <SOURCE_CONNECTION> -f data.sql --use-copy --data-only -x "storage.buckets_vectors" -x "storage.vector_indexes"
   ```

4. Confirm all three files exist, are non-empty, are readable only by the intended operator, and are copied to encrypted off-site storage. Do not commit them.

## Restore rehearsal

1. Create a brand-new disposable Supabase project and copy its target connection string.
2. Confirm the target project reference and hostname do not match the source.
3. Restore with the Supabase-documented single-transaction command:

   ```text
   psql --single-transaction --variable ON_ERROR_STOP=1 --file roles.sql --file schema.sql --command "SET session_replication_role = replica" --file data.sql --dbname <DISPOSABLE_TARGET_CONNECTION>
   ```

4. Apply or reconcile the repository EF migration history only according to the reviewed migration state. Do not use `migration repair` without first proving why the recorded and actual schemas differ.
5. Point a local tracker process at the disposable restored target and verify:
   - `/health/ready` is Healthy;
   - expected user, company, application, history, contact, task, appointment, administrative-audit, and retention-run counts match the source rehearsal snapshot;
   - a verified test user can sign in and open owned records;
   - another test owner cannot access them;
   - Saved and deletion-scheduled states are unchanged;
   - the public demo still contains synthetic data only;
   - the retention verification SQL reports the expected scheduler state.
6. Delete the disposable project only after recording a successful result and retaining the encrypted backup according to the chosen retention policy.

## Rehearsal record

Record the date, operator, source environment label (not its secret connection string), disposable target label, backup file checksums, row-count comparison, migration state, functional checks, failures, corrective work, and final result. The repository cannot safely manufacture this record without access to an explicitly disposable target.
