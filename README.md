# Job Application Tracker

A private ASP.NET Core job-search organizer for tracking applications, follow-ups, appointments, and expiring records from one focused dashboard.

## Current status

Milestone 4 adds deterministic pasted-text extraction to the private tracking workflow. A verified user can paste a job advertisement, review evidence-backed suggestions, correct every field, and explicitly confirm before an application is created.

Safe URL fetching, Dashboard, Action Centre, settings, contacts, tasks, appointments, retention automation, and the public demo remain scoped to their later milestones.

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
| `/dashboard` | Authenticated three-month dashboard placeholder |
| `/applications` | Searchable, filterable private application library |
| `/applications/new` | Manual application entry |
| `/applications/import/text` | Paste a job description for deterministic extraction |
| `/applications/import/{id}/review` | Review and correct an owner-scoped extraction draft |
| `/applications/{id}` | Private application details and saved-state control |
| `/companies` | Searchable private company directory |
| `/companies/new` | Manual company entry |
| `/actions` | Authenticated Action Centre placeholder |
| `/settings` | Authenticated settings placeholder |
| `/demo` | Public synthetic demo placeholder |
| `/health` | Minimal plain-text health response |

The registration page is public, but account creation requires an unexpired, unused, unrevoked invitation code.

## Manual tracking

For manual entry, create companies separately and select one while adding or editing an application. Pasted-text review accepts a company name and location on the same page: an exact owner-scoped match is reused, otherwise the company is created only when the reviewed application is confirmed. A company cannot be deleted while applications still reference it.

New applications begin at the `Applied` pipeline stage with an `Active` outcome and receive an initial append-only history event. Stage and outcome transitions arrive in Milestone 6.

New applications default to not saved. **Saved** applications are exempt from future automatic retention deletion, and the setting can be changed from the application list, details page, or edit form. Milestone 8 will add the visible Chopping Block grace period and scheduled cleanup; changing the setting in Milestone 3 never immediately deletes anything.

## Pasted-text extraction

The extractor normalizes pasted text and reads explicit labels such as `Job Title`, `Company`, `Location`, `Employment Type`, `Salary`, `Job Reference`, and `Closing Date`. It also recognises corroborated stacked job-board headers, Australian location formats, standalone pay lines, multi-line metadata headings, common platform title phrases, posting bylines, and explicit applications-close or apply-by sentences. Salary suggestions pass field-specific plausibility checks, so ratings and review counts are ignored while annual ranges, `70k–80k`, hourly rates, and daily rates remain supported. Every detected field includes a high- or medium-confidence evidence note. Uncertain values remain blank rather than being guessed.

Review drafts are private, owner-scoped, and expire after 24 hours. Cancelling removes the draft and creates no application. Confirming stores the original pasted text, the reviewed values, the initial Applied/Active history event, and any new company in one database operation. Extraction is deterministic and makes no external AI or network call.

## Database setup

Store `ConnectionStrings:DefaultConnection` with .NET user secrets. Use the Supabase Session Pooler on port 5432 with TLS required. Never place the database password in this repository or paste it into an issue.

Restore the repository-local migration tool and apply reviewed migrations:

```powershell
dotnet tool restore
dotnet ef database update --project src/JobTracker.Web
```

The migrations create ASP.NET Core Identity tables, owner-aware private data tables, and one-time invitations. Composite foreign keys include the owner identifier to reject cross-owner relationships at the database boundary.

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
