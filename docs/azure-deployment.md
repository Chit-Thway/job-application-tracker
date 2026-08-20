# Azure production deployment

This runbook deploys the tracker to the HTTPS `azurewebsites.net` address supplied by Azure App Service. A custom domain is optional and deliberately deferred until the default address passes production acceptance.

## Production shape

- One Linux Azure App Service running .NET 10. Public QA begins on Free F1 at the
  default Azure hostname; upgrade to the smallest suitable Basic plan before the
  site needs dependable uptime, a custom domain, or Always On.
- One separate production Supabase PostgreSQL project. Do not point public QA traffic at the development or restore-test database.
- Azure Communication Services Email with an Azure Managed Domain for verification and password-reset messages.
- A system-assigned App Service managed identity for passwordless access to Communication Services.
- GitHub Actions deployment authenticated with OpenID Connect; publish profiles and long-lived Azure client secrets are not used.

## Resource names

Names can be adjusted when Azure reports that a globally unique name is unavailable. Keep every resource in one production resource group where possible.

| Resource | Suggested name |
|---|---|
| Resource group | `job-tracker-production` |
| App Service plan | `job-tracker-production-plan` |
| Web app | `chit-thway-job-tracker` |
| Communication Services | `job-tracker-communication` |
| Email Communication Service | `job-tracker-email` |

Use **Australia East** for the web app unless the Azure portal shows a materially better Australian option. Communication Services and Email Communication Service must use compatible data geographies.

## Provisioning order

1. Confirm the Azure subscription and spending limit. Use Free F1 for the initial public QA period, and inspect the live Basic B1 price before upgrading to paid compute.
2. Create the resource group, Linux App Service plan, and .NET 10 web app. Keep basic deployment authentication and FTPS disabled.
3. Create the separate production Supabase project. Run `scripts/Provision-ProductionDatabase.ps1`; it validates the Session Pooler target, applies reviewed Entity Framework migrations, configures and verifies retention cron, and saves the database connection as an App Service setting without printing it.
4. Create Communication Services and Email Communication Service resources. Provision the one-click Azure Managed Domain and connect that verified domain to Communication Services.
5. Enable the web app's **system-assigned managed identity**. On the Communication Services resource, grant that identity the minimum role that permits email sending. Do not copy an ACS access key into the web app.
6. Configure the web app settings listed below. Verify `/health/ready` directly on Free F1; configure it as the platform health-check path after upgrading to a tier that supports the feature.
7. In the GitHub repository, create a protected `production` environment. Add the OIDC identity values as environment secrets and the web-app name as an environment variable.
8. Apply production migrations deliberately before the first deployment. Run the manual **Deploy production** workflow only from a reviewed `main` commit.

## Required App Service settings

App Service settings are environment configuration, not source-controlled values. Mark the database value as a deployment-slot setting if a slot is added later.

| Setting | Value |
|---|---|
| `ASPNETCORE_ENVIRONMENT` | `Production` |
| `ASPNETCORE_FORWARDEDHEADERS_ENABLED` | `true` |
| `ConnectionStrings__DefaultConnection` | Production Supabase Session Pooler connection string with TLS required |
| `Email__Endpoint` | Communication Services HTTPS endpoint, for example `https://name.communication.azure.com` |
| `Email__SenderAddress` | Exact MailFrom address displayed by the connected Azure Managed Domain |
| `Email__PublicBaseUrl` | Public HTTPS origin without a trailing path, for example `https://name.azurewebsites.net` |
| `Email__SupportAddress` | Support mailbox displayed as a `mailto:` link in account messages |
| `AllowedHosts` | The assigned host, for example `chit-thway-job-tracker.azurewebsites.net` |

Never put the database password, invitation code, email token, publish profile, or Azure access key in GitHub text, workflow YAML, logs, screenshots, or support messages.

## GitHub production environment

Create an environment named `production` and require approval before deployment. Configure:

- secret `AZURE_CLIENT_ID`;
- secret `AZURE_TENANT_ID`;
- secret `AZURE_SUBSCRIPTION_ID`; and
- variable `AZURE_WEBAPP_NAME`.

The identity represented by these values should be federated only to this repository's `production` environment and scoped to the production web app or resource group. The workflow requests a short-lived OpenID Connect token for each deployment.

## First production acceptance

1. Open the Azure HTTPS address and confirm the public landing page and `/demo` load without authentication.
2. Confirm `/health/live` and `/health/ready` return HTTP 200 with minimal Healthy JSON.
3. Confirm signed-out visitors are redirected away from private routes and cannot mutate `/demo`.
4. Create a single production invitation from a controlled operator shell with `--email`, register the mailbox using the delivered one-time code, receive the real verification email, verify it, and sign in.
5. Request a password reset and confirm the real message completes the reset without leaking a token into logs.
6. Exercise application creation, Saved state, status history, dashboard, owner isolation, and retention scheduling.
7. Restart the web app and confirm existing sign-in behavior, database access, health, and email still work.
8. Record the deployed commit, migration list, health evidence, email evidence, rollback point, and Chit-Thway's acceptance.

## Rollback

Do not reverse applied migrations. For an application regression, redeploy the last accepted Git commit and verify readiness. For a data/schema incident, follow `docs/backup-restore.md` using a verified backup and a separate target before changing production.
