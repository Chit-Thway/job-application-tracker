# Job Application Tracker

A private ASP.NET Core job-search organizer for tracking applications, follow-ups, appointments, and expiring records from one focused dashboard.

## Current status

Milestone 8 adds transparent retention review and traffic-independent database cleanup. Eligible unsaved records receive a professional **Deletion scheduled** state with an exact 14-day deadline and a direct Save escape route across the library, details, dashboard, Action Centre, and Settings. The application library also supports remembered card/list views plus an explicit, owner-scoped selection mode for confirmed bulk deletion, pipeline-stage changes, and append-only notes.

Ghosting remains a deliberate user decision: the tracker suggests a follow-up after 14 days without a meaningful employer response and offers confirmation after 30 days, but never changes the outcome automatically. Status history, contacts and interactions, tasks, appointments, manual entry, deterministic extraction, safe public-URL import, and the explicit-click browser extension remain available throughout the workflow. The public synthetic demo remains scoped to a later milestone.

## Technology

- .NET 10 LTS
- ASP.NET Core MVC with server-rendered Razor Views
- C#
- Entity Framework Core with Npgsql
- Supabase-hosted PostgreSQL
- ASP.NET Core Identity
- Custom responsive CSS
- xUnit unit and integration tests
- GitHub Actions

Supabase provides PostgreSQL only; ASP.NET Core Identity owns authentication and application sessions.

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- Git

The repository pins its SDK feature band in `global.json` and allows compatible patch roll-forward.

## Run locally

From the repository root:

```powershell
dotnet restore
dotnet run --project src/JobTracker.Web --launch-profile http
```

Open [http://localhost:5261](http://localhost:5261). Stop the application with `Ctrl+C`.

To use the local HTTPS profile, trust the ASP.NET Core development certificate once:

```powershell
dotnet dev-certs https --trust
dotnet run --project src/JobTracker.Web --launch-profile https
```

Then open [https://localhost:7239](https://localhost:7239).

## Main routes

| Route | Current behavior |
|---|---|
| `/` | Branded foundation landing page |
| `/account/login` | Private account sign-in |
| `/account/register` | Account creation with a private one-time invitation |
| `/account/forgot-password` | Password-reset request |
| `/account/resend-verification` | Email-verification resend |
| `/dev/mail` | Local-only verification/reset message sink |
| `/dashboard` | Authenticated current-and-prior-two-calendar-month dashboard |
| `/applications` | Searchable, filterable card/list library with explicit bulk-selection actions |
| `/applications/new` | Manual application entry |
| `/applications/import/url` | Safely import a public HTML job page into a review draft |
| `/applications/import/text` | Paste a job description for deterministic extraction |
| `/applications/import/extension` | Receive an active-tab browser capture into a private review draft |
| `/applications/import/{id}/review` | Review and correct an owner-scoped extraction draft |
| `/applications/{id}` | Complete private workflow: status history, collapsible contacts/tasks/appointments, readable job description, posting context, and saved state |
| `/companies` | Searchable private company directory |
| `/companies/new` | Manual company entry |
| `/actions` | Overdue tasks, follow-up warnings, Ghosted decisions, scheduled-deletion warnings, and upcoming appointments |
| `/settings` | Authenticated retention policy, owner-scoped counts, and scheduler health |
| `/demo` | Public synthetic demo placeholder |
| `/health` | Minimal plain-text health response |

The registration page is public, but account creation requires an unexpired, unused, unrevoked invitation code.

## Manual tracking

For manual entry, create companies separately and select one while adding or editing an application. Pasted-text review accepts a company name and location on the same page: an exact owner-scoped match is reused, otherwise the company is created only when the reviewed application is confirmed. A company cannot be deleted while applications still reference it.

New applications begin at the `Applied` pipeline stage with an `Active` outcome and receive an initial append-only history event. Every later stage or outcome change adds another timestamped event instead of replacing the historical trail.

New applications default to not saved. **Saved** applications are exempt from future automatic retention deletion, and the setting can be changed from the application list, details page, or edit form. Milestone 8 adds a visible **Deletion scheduled** grace period and automated cleanup; changing the setting never immediately deletes anything.

The application library defaults to the familiar two-column card view and remembers an optional compact list view in local browser storage. Selection mode supports manual selection or all-shown, saved, unsaved, and deletion-scheduled presets. Bulk stage changes append status history, bulk notes append a dated history entry without overwriting existing notes, and bulk deletion always opens a separate permanent-action review page.

## Complete tracking workflow

The application details page is the operational home for a job opportunity. Status and outcome changes are appended to its timeline with an optional note. Calls, emails, messages, meetings, and notes appear in the same chronological history. Marking an interaction as an employer response derives the first-response time; correcting or deleting that interaction recalculates the value. Empty contact, task, and appointment sections start collapsed; sections with saved records start expanded but remain collapsible.

Contacts are owner-scoped and may be linked only to their own application. Follow-up tasks can have a local due time and can be completed, reopened, or deleted. Interviews, calls, assessments, and other appointments preserve the user's configured timezone while storing their instants in UTC. The sidebar promotes the next open task or upcoming appointment so the user does not have to reconstruct the next step from several pages. A stored description appears between reviewed posting details and the original source; its popout separates recognised section headings, paragraphs, and lists without executing source HTML.

## Pasted-text extraction

The extractor normalizes pasted text and reads explicit labels such as `Job Title`, `Company`, `Location`, `Employment Type`, `Salary`, `Job Reference`, and `Closing Date`. It also recognises corroborated stacked job-board headers, Australian location formats, standalone pay lines, multi-line metadata headings, common platform title phrases, posting bylines, and explicit applications-close or apply-by sentences. Salary suggestions pass field-specific plausibility checks, so ratings and review counts are ignored while annual ranges, `70k–80k`, hourly rates, and daily rates remain supported. Every detected field includes a high- or medium-confidence evidence note. Uncertain values remain blank rather than being guessed.

Review drafts are private, owner-scoped, and expire after 24 hours. Cancelling removes the draft and creates no application. Confirming stores the original pasted text, the editable job description, the reviewed values, the initial Applied/Active history event, and any new company in one database operation. Description extraction is intentionally broad while structured fields retain their conservative rules. Extraction is deterministic and makes no external AI or network call.

## Public-URL extraction

URL import allows only public HTTP or HTTPS pages with default ports. It rejects credentials in URLs and blocks loopback, private, link-local, metadata, documentation, multicast, transition, and other non-public IPv4/IPv6 destinations. DNS answers are checked before every request and redirect and checked again when the production socket connects. Redirects are manual and limited; browser cookies, credentials, authorization, referrer, and proxy credentials are not forwarded. Responses must be HTML, complete within the configured timeout, and remain under the decompressed size limit.

The HTML parser reads official Schema.org `JobPosting` JSON-LD first, including hiring organisation, title, location, employment type, base salary, identifier, application contact, work mode, `validThrough`, and the job description. Page metadata and visible text then feed the existing deterministic rules as fallbacks. If a page blocks automated access or cannot be imported safely, the form keeps the URL visible and offers pasted-text and manual-entry alternatives.

## Browser extension capture

The unpacked Manifest V3 extension in `browser-extension` is the easiest option for script-heavy or automation-blocking job boards. It reads Schema.org `JobPosting` data, the readable description, and rendered job fields from the active tab only after the user clicks **Capture and review**. It includes selected-job-panel support for SEEK and Indeed, including hourly pay such as `$35–$40 an hour`. It does not fetch the page again, execute page-owned scripts, contact an AI service, or save an application directly.

Install it locally:

1. Start the tracker, sign in, and leave it running.
2. Open `chrome://extensions` in Chrome or `edge://extensions` in Edge.
3. Enable **Developer mode**, choose **Load unpacked**, and select the repository's `browser-extension` directory.
4. Pin **Job Application Tracker Capture** to the toolbar.
5. Open a job advertisement, click the extension, and choose **Capture and review**.

The extension defaults to `http://localhost:5261`; its popup can remember a different tracker address. Non-local tracker addresses must use HTTPS. The manifest requests only `activeTab`, `scripting`, and `storage`: there are no broad host permissions, content scripts, background workers, analytics, or remote APIs. The capture is handed to the authenticated tracker through a URL fragment, removed immediately from browser history, validated by the server, and stored only as an owner-scoped 24-hour review draft. See `browser-extension/README.md` for the focused install and privacy guide.

## Database setup

Store `ConnectionStrings:DefaultConnection` with .NET user secrets. Use the Supabase Session Pooler on port 5432 with TLS required. Never place the database password in this repository or paste it into an issue.

Restore the repository-local migration tool and apply reviewed migrations:

```powershell
dotnet tool restore
dotnet ef database update --project src/JobTracker.Web
```

The migrations create ASP.NET Core Identity tables, owner-aware private data tables, and one-time invitations. Composite foreign keys include the owner identifier to reject cross-owner relationships at the database boundary.

## Automatic retention cleanup

Unsaved applications become eligible on the three-calendar-month anniversary of their application date in the owner's configured timezone. The first retention run after eligibility assigns an exact deletion time 14 full days later. Saving at any point cancels that time immediately; unsaving an already-old application starts a fresh 14-day grace period.

Milestone 8 places cleanup in the database so it does not depend on website traffic. After applying the migration, open the Supabase SQL editor and run `database/supabase/configure-retention-cron.sql` once. It enables Supabase Cron and schedules the atomic cleanup function hourly at minute 17. Use `database/supabase/verify-retention-cron.sql` to inspect the job and its privacy-safe operational history. Retention runs store timestamps, counts, success state, and a short database error code only; they never retain deleted role titles, company names, notes, or source text.

The application list, application details, dashboard, and Action Centre show every application with **Deletion scheduled**, its exact deletion time, and a Save action. The database function re-checks both the saved state and due time inside the deletion transaction before cascading dependent records.

## Controlled local account setup

Configure the initial development owner account through user secrets:

```powershell
dotnet user-secrets set "BootstrapAccount:Email" "your-email@example.com" --project src/JobTracker.Web
dotnet user-secrets set "BootstrapAccount:DisplayName" "Your name" --project src/JobTracker.Web

$trackerAccountPassword = Read-Host "Choose a strong local account password" -AsSecureString
$trackerPlainPassword = [System.Net.NetworkCredential]::new("", $trackerAccountPassword).Password
dotnet user-secrets set "BootstrapAccount:Password" $trackerPlainPassword --project src/JobTracker.Web
Remove-Variable trackerAccountPassword, trackerPlainPassword -ErrorAction SilentlyContinue
```

Start the application, open `/dev/mail` on localhost, and use the verification link. The local message sink is available only in Development and keeps messages in memory.

After the account is verified, remove the temporary bootstrap settings:

```powershell
dotnet user-secrets remove "BootstrapAccount:Email" --project src/JobTracker.Web
dotnet user-secrets remove "BootstrapAccount:DisplayName" --project src/JobTracker.Web
dotnet user-secrets remove "BootstrapAccount:Password" --project src/JobTracker.Web
```

## One-time invitations

After the initial account exists, create additional accounts through private one-time invitations. Stop the running web application, then create a code from the repository root:

```powershell
dotnet run --project src/JobTracker.Web -- invitations create --days 7
```

The lifetime can be from 1 to 30 days. The readable code is printed once; give it privately to its intended recipient and do not paste it into GitHub, logs, or chat. The database stores only its SHA-256 fingerprint, so the readable code cannot be recovered later.

List invitation IDs and states without revealing their codes:

```powershell
dotnet run --project src/JobTracker.Web -- invitations list
```

Revoke an available invitation by ID:

```powershell
dotnet run --project src/JobTracker.Web -- invitations revoke 00000000-0000-0000-0000-000000000000
```

Start the web application normally, open `/account/register`, and enter the code. Account creation and code consumption occur in one protected database transaction. The code cannot be reused, and the new account remains blocked from private pages until its email is verified through the local `/dev/mail` message.

## Quality checks

Run the same checks used by continuous integration:

```powershell
dotnet restore --locked-mode
dotnet format --verify-no-changes --no-restore
dotnet build --configuration Release --no-restore
dotnet test --configuration Release --no-build
dotnet publish src/JobTracker.Web --configuration Release --no-build --output artifacts/publish
```

Build warnings are treated as errors. Package lock files make dependency restores repeatable.

## Repository structure

```text
JobTracker.sln
src/
  JobTracker.Web/                 ASP.NET Core MVC application
browser-extension/                Unpacked Chrome/Edge active-tab capture extension
tests/
  JobTracker.UnitTests/           Fast foundation and domain tests
  JobTracker.IntegrationTests/    In-process HTTP and security checks
```

Browser tests will be added when the product has critical interactive journeys.

## Configuration and secrets

- Do not commit credentials, connection strings, tokens, or personal job-search information.
- Use [.NET user secrets](https://learn.microsoft.com/aspnet/core/security/app-secrets) for local credentials.
- Use environment or host-managed settings in deployed environments.
- The private planning directory is deliberately excluded by `.gitignore`.

## Product direction

The MVP will provide a private authenticated tracker and a separate public read-only synthetic demo. It will cover applications, companies, status history, contacts and interactions, tasks, appointments, three-calendar-month dashboard reporting, ghosting warnings, and transparent automatic retention for old unsaved applications.

Built for Chit-Thway.
