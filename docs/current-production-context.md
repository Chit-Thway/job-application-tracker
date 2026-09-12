# Current production context

Last reviewed: 12 September 2026. Read this file before searching historical milestones or reconstructing deployment details.

## Current stage

- Production is live at [myjobtracker.com.au](https://myjobtracker.com.au).
- `https://www.myjobtracker.com.au` and the original Azure hostname redirect public traffic to the canonical domain. Health endpoints remain available for operations.
- Registration is open. New accounts verify a six-digit email code and start on Tier 1 with a maximum of 10 stored applications.
- Tier 2 is unlimited. Administrators can review accounts, change tiers, lock access, resend verification, and delete eligible non-admin accounts.
- Phone numbers and invitation codes are not collected or used.
- The Chrome extension source is version 1.0.4 and defaults to the custom domain. Publishing 1.0.4 to the Chrome Web Store is a separate store action.
- The default branch is `main`. Use GitHub or `git branch -avv` for the current branch list.
- The public demo and extension-navigation work reached `main` through PRs #26 and #32. The original branches are retained, and the September reconciliation records their ancestry in chronological order. See the [branch and deployment timeline](branch-history-reconciliation.md).
- The extension autofill upgrade is [planned](extension-autofill-plan.md); implementation has not started.

Historical milestones and portfolio notes are in `docs/project-process-candidates.md` and Git history. Consult them only when a task specifically needs history.

## Service and login map

Identifiers below are not secrets. Sessions can expire and may need the user to sign in again.

| Service | Current identifier and access method |
|---|---|
| GitHub | Account `Chit-Thway`; repository [Chit-Thway/job-application-tracker](https://github.com/Chit-Thway/job-application-tracker); use the authenticated `gh` CLI session |
| Azure | Login `redacted@example.invalid`; subscription `Azure subscription 1` (`5cdac30c-1ef1-4208-9c61-0036b8ee9eb8`); tenant `22a4cd1f-9811-4d27-9378-eaa72117adac`; use the authenticated `az` CLI session |
| Production app | Resource group `job-tracker-production`; plan `job-tracker-production-plan`; Linux App Service `chit-thway-job-tracker`; B1 with Always On |
| Azure email | Communication service `job-tracker-communication`; email service `job-tracker-email`; support/admin email `redacted@example.invalid` |
| Certificates | App Service managed certificates `myjobtracker-com-au-managed` and `www-myjobtracker-com-au-managed` |
| Supabase | Production PostgreSQL connection is held only in Azure App Service setting `ConnectionStrings__DefaultConnection`; Supabase CLI is not installed locally |
| Domain registrar | Crazy Domains manages `myjobtracker.com.au`; use the existing signed-in browser session when available |

## Secret handling

Do not add passwords or service secrets to this file or Git. In particular:

- Azure and GitHub authentication stays in their CLI credential stores.
- The Supabase connection string stays in Azure App Service configuration.
- Email service credentials and sender configuration stay in Azure.
- The application admin password is not stored in the repository; only the non-secret account email is recorded above.
- Crazy Domains credentials stay in the registrar/browser session.

## Production workflow

- GitHub Quality validates code pull requests once. Markdown-only pull requests are ignored, and merging to `main` does not repeat the same quality run.
- Production deployment is the manual `Deploy production` workflow from reviewed `main`.
- `CanonicalOrigin`, `Email__PublicBaseUrl`, and `AllowedHosts` are configured in Azure App Service.
- Choose task verification from `docs/test-strategy.md`; do not default to the full suite locally.
