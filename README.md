# Job Application Tracker

A private ASP.NET Core job-search organizer for tracking applications, follow-ups, appointments, and expiring records from one focused dashboard.

## Current status

Milestone 1 establishes the application foundation. The branded shell, responsive navigation, placeholder routes, friendly errors, health endpoint, automated tests, and GitHub quality workflow are present.

There is intentionally **no login, database connection, or real application data yet**. Those capabilities begin in later milestones.

## Technology

- .NET 10 LTS
- ASP.NET Core MVC with server-rendered Razor Views
- C#
- Custom responsive CSS
- xUnit unit and integration tests
- GitHub Actions

The planned data layer is Entity Framework Core with Supabase-hosted PostgreSQL. ASP.NET Core Identity will provide authentication. Neither is connected during Milestone 1.

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

## Foundation routes

| Route | Milestone 1 behavior |
|---|---|
| `/` | Branded foundation landing page |
| `/dashboard` | Three-month dashboard placeholder |
| `/applications` | Applications library placeholder |
| `/applications/new` | Add-application placeholder |
| `/actions` | Action Centre placeholder |
| `/settings` | Settings placeholder |
| `/demo` | Public synthetic demo placeholder |
| `/health` | Minimal plain-text health response |

Every unfinished product route labels itself as a foundation preview. It does not pretend that data or authentication exists.

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
- Use [.NET user secrets](https://learn.microsoft.com/aspnet/core/security/app-secrets) for local secrets when configuration begins.
- Use environment or host-managed settings in deployed environments.
- The private planning directory is deliberately excluded by `.gitignore`.

## Product direction

The MVP will provide a private authenticated tracker and a separate public read-only synthetic demo. It will cover applications, companies, status history, contacts and interactions, tasks, appointments, three-calendar-month dashboard reporting, ghosting warnings, and transparent automatic retention for old unsaved applications.

Built for Chit-Thway.
