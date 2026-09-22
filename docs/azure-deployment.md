# Deployment and hosting

The production tracker runs at [myjobtracker.com.au](https://myjobtracker.com.au). This guide describes the deployment shape and release checks; operator accounts, resource identifiers, and credentials are kept outside the repository.

## Production services

| Service | Purpose |
|---|---|
| Azure App Service on Linux | Hosts the .NET 10 web application and its custom domain. |
| Supabase PostgreSQL | Stores accounts and private tracker data in a separate production database. |
| Azure Communication Services Email | Sends verification and password-reset messages through the app's managed identity. |
| GitHub Actions | Validates pull requests and deploys reviewed `main` commits using short-lived OpenID Connect credentials. |

Production database credentials, email configuration, and identity values are supplied through Azure and GitHub environment settings. They must not be committed or printed in logs. Local development uses a different database and .NET user secrets.

## Configuration boundaries

The App Service configuration supplies `ASPNETCORE_ENVIRONMENT`, `ConnectionStrings__DefaultConnection`, the `Email__*` values, `CanonicalOrigin`, and `AllowedHosts`. The repository contains setting names and validation rules, not production values. Deployment access is scoped to the GitHub `production` environment and the intended Azure resources.

## Release sequence

1. Review the change and run the checks selected by the [test strategy](test-strategy.md). GitHub Quality runs the full code checks on the pull request.
2. Merge the passing pull request into `main`.
3. If the release includes a schema change, apply its reviewed Entity Framework migration to the production database before deploying code that needs it.
4. Start the manual `Deploy production` workflow from the merged `main` commit. The workflow restores locked dependencies, checks formatting, builds, tests, publishes, and deploys the package.
5. Confirm `/health/live` and `/health/ready` return Healthy, the public demo loads without sign-in, and the changed user journey works.

## Recovery

For an application regression, redeploy the last accepted commit and verify readiness. Do not reverse a production migration as an automatic rollback. For a data or schema incident, follow the [backup and restore guide](backup-restore.md) with a verified backup and a separate target before changing production data.
