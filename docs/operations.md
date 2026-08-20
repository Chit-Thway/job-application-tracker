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

## Controlled users

Stop the web process before running invitation commands so the CLI owns the process cleanly.

```powershell
dotnet run --project src/JobTracker.Web -- invitations create --days 7
dotnet run --project src/JobTracker.Web -- invitations create --days 7 --email recipient@example.com
dotnet run --project src/JobTracker.Web -- invitations list
dotnet run --project src/JobTracker.Web -- invitations revoke <invitation-id>
```

Without `--email`, codes are shown once and must be delivered privately. With `--email`, the configured provider sends a branded invitation and the readable code is not printed. The database stores only a SHA-256 fingerprint. Never place readable codes in GitHub, logs, screenshots, or support messages.

Outside Development, invitation commands require two deliberate controls: the temporary setting `InvitationCommands:Enabled=true` and the explicit `--confirm-production` argument. There is no browser-accessible invitation administration endpoint. Run the published application from a controlled operator shell with its normal production connection configuration:

```powershell
$env:InvitationCommands__Enabled = "true"
dotnet JobTracker.Web.dll invitations create --days 7 --confirm-production
Remove-Item Env:InvitationCommands__Enabled
```

Use the same final argument for `list` and `revoke`. Remove the temporary setting immediately after the command, do not leave an invitation command running beside the web process, and never copy the production connection string or readable code into shell history. The Azure-specific operator invocation will be finalized with the deployment in Milestone 11.

## Email

Development uses an in-memory message sink at `/dev/mail`; it disappears on restart and is unavailable outside Development. Production uses Azure Communication Services Email through the App Service managed identity. Configure the HTTPS endpoint, exact MailFrom address, public application URL, and support address with App Service settings, grant the web app identity email-sending access, and test invitation, verification, and password-reset messages from an external mailbox. Never log recipients, invitation codes, tokens, or complete action URLs.

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

1. **Contain:** disable the affected deployment or feature, revoke available invitations, and preserve privacy-safe logs and timestamps.
2. **Rotate:** rotate database, email, deployment, and other possibly exposed credentials. Invalidate sessions/Data Protection keys when the incident requires it, understanding that users will be signed out and outstanding action links can become invalid.
3. **Assess:** use request IDs and database ownership boundaries; do not export unrelated private job data.
4. **Recover:** deploy a reviewed commit, apply only reviewed migrations, or restore using `docs/backup-restore.md`.
5. **Verify:** run health, authentication, owner-isolation, retention, demo, and backup checks before reopening access.
6. **Document:** record impact, cause, containment, recovery, and prevention without including credentials or private application text.

## Shutdown and restart

Use `Ctrl+C` for a local process. After restart, verify `/health/live`, `/health/ready`, sign-in, dashboard, and the public demo. A restart must not be used to repair migration or retention failures silently.
