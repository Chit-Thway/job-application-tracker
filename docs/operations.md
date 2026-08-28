# Operations runbook

This runbook covers setup and ongoing operation. Azure resource creation and first production acceptance are detailed in `docs/azure-deployment.md`.

## Fresh setup

1. Install the pinned .NET 10 SDK from `global.json`, Git, and the repository-local EF tool (`dotnet tool restore`).
2. Create a non-production Supabase PostgreSQL project. Copy its Session Pooler connection values and keep TLS required.
3. Store the connection string outside Git:

   ```powershell
   dotnet user-secrets set "ConnectionStrings:DefaultConnection" "<session-pooler-connection-string>" --project src/JobTracker.Web
   ```

4. Restore and build from locked dependencies:

   ```powershell
   dotnet restore --locked-mode
   dotnet build --configuration Release --no-restore
   ```

5. Review pending EF migrations, then apply them:

   ```powershell
   dotnet ef migrations list --project src/JobTracker.Web
   dotnet ef database update --project src/JobTracker.Web
   ```

6. Configure and verify the retention scheduler using `database/supabase/configure-retention-cron.sql` and `database/supabase/verify-retention-cron.sql`.
7. Create the initial Development owner using the README's user-secret bootstrap steps, verify it through `/dev/mail`, then remove all three bootstrap secrets.
8. Run the complete quality gate from the README and start the application. `/health/live` and `/health/ready` must both return HTTP 200 with `Healthy` JSON.

## Account registration and tiers

Registration is public at `/account/register`. New accounts start on Tier 1 and cannot sign in until the six-digit email code is verified. Codes expire after ten minutes, allow five failed attempts, and have a server-enforced 30-second resend delay. Administrators can resend a code from the account page, but the same cooldown applies.

Use `/admin/users` to review identity metadata, lock or unlock an account, or assign Tier 1/Tier 2. Admin tools do not expose private applications. The former invitation commands and invitation administration page have been removed.

## Email

Development uses an in-memory message sink at `/dev/mail`; it disappears on restart and is unavailable outside Development. Production uses Azure Communication Services Email through the App Service managed identity. Configure the HTTPS endpoint, exact MailFrom address, public application URL, and support address with App Service settings, grant the web app identity email-sending access, and test email verification codes and password-reset messages from an external mailbox. Never log recipients, readable verification codes, tokens, or complete action URLs.

Azure Communication Services Email is an outbound delivery service, not an inbox. The application logs only the Azure operation ID. To retain message and recipient delivery evidence, configure Azure Monitor diagnostic settings for Email Send Mail and Email Status Update logs and choose a Log Analytics workspace or storage destination. Logging begins only after the diagnostic setting is enabled and can add Azure ingestion/storage charges.

## Health and diagnostics

- `/health/live`: the process can answer requests; it does not contact the database.
- `/health/ready`: the configured database can be reached.
- `/health`: readiness alias for hosting systems expecting a conventional path.

Health output contains check names and states only. Request logs contain a generated request ID, method, endpoint template/display name, status, and duration. Use the request ID to correlate a sanitized report; do not add query strings, form values, cookies, identities, role titles, company names, or imported source text to logging.

## Retention operations

The Supabase Cron job should run hourly at minute 17. The verification SQL reports whether it is scheduled and shows privacy-safe retention-run timestamps, counts, success state, and short error code. If a run fails:

1. Pause manual destructive work and capture the time and database error code.
2. Confirm the database is reachable and the migration containing the cleanup function is applied.
3. Re-run the verification SQL.
4. Re-enable or invoke only the reviewed idempotent cleanup function; never hand-delete a broad set of application rows.
5. Confirm Saved records and not-yet-due records remain, then record the sanitized outcome.

## Incident response

1. **Contain:** disable the affected deployment or feature, lock affected accounts when appropriate, and preserve privacy-safe logs and timestamps.
2. **Rotate:** rotate database, email, deployment, and other possibly exposed credentials. Invalidate sessions/Data Protection keys when the incident requires it, understanding that users will be signed out and outstanding action links can become invalid.
3. **Assess:** use request IDs and database ownership boundaries; do not export unrelated private job data.
4. **Recover:** deploy a reviewed commit, apply only reviewed migrations, or restore using `docs/backup-restore.md`.
5. **Verify:** run health, authentication, owner-isolation, retention, demo, and backup checks before reopening access.
6. **Document:** record impact, cause, containment, recovery, and prevention without including credentials or private application text.

## Shutdown and restart

Use `Ctrl+C` for a local process. After restart, verify `/health/live`, `/health/ready`, sign-in, dashboard, and the public demo. A restart must not be used to repair migration or retention failures silently.
