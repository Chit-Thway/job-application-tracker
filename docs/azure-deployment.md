# Deployment and hosting

The production tracker runs at [myjobtracker.com.au](https://myjobtracker.com.au). This guide describes the deployment shape and release checks; operator accounts, resource identifiers, and credentials are kept outside the repository.

## Production services

| Service | Purpose |
|---|---|
| Azure Container Apps | Hosts the .NET 10 web application on the Consumption profile and serves the custom domain over HTTPS. |
| Supabase PostgreSQL | Stores accounts and private tracker data in a separate production database. |
| Azure Communication Services Email | Sends verification and password-reset messages through the app's managed identity. |
| GitHub Actions and GitHub Container Registry | Validate reviewed `main` commits, publish release images, and deploy by digest using short-lived OpenID Connect credentials. |

Production database credentials, email configuration, and identity values are supplied through Azure and GitHub environment settings. They must not be committed or printed in logs. Local development uses a different database and .NET user secrets.

## Configuration boundaries

The Container App configuration supplies `ASPNETCORE_ENVIRONMENT`, `ConnectionStrings__DefaultConnection`, the `Email__*` values, `CanonicalOrigin`, and `AllowedHosts`. Store the database connection string as a Container Apps secret and reference it from the environment variable. Enable Azure's shared ASP.NET Core Data Protection keys so sign-in cookies and form tokens survive replica restarts. Deployment access is scoped to the GitHub `production` environment and the production Container App. That GitHub environment supplies the `AZURE_CONTAINER_APP` and `AZURE_RESOURCE_GROUP` variables.

The app listens on port 8080 and uses an HTTP scaling rule with zero minimum replicas. The first request after an idle interval can take longer. HTTPS readiness checks exercise both the app and PostgreSQL. The registry image is built only from the public source tree. Production settings and credentials are supplied separately at runtime.

## Release sequence

1. Review the change and run the checks selected by the [test strategy](test-strategy.md). GitHub Quality runs the full code checks on the pull request.
2. Merge the passing pull request into `main`.
3. If the release includes a schema change, apply its reviewed Entity Framework migration to the production database before deploying code that needs it.
4. Start the manual `Deploy production` workflow from the merged `main` commit. The workflow restores locked dependencies, checks formatting, builds, tests, publishes the container, and deploys its digest. Use `publish_only` when bootstrapping the Container App and you need the validated image before the target exists.
5. Confirm `/health/live` and `/health/ready` return Healthy, the public demo loads without sign-in, and the changed user journey works.

## Recovery

For an application regression, deploy the last accepted image digest and verify readiness. Do not reverse a production migration as an automatic rollback. For a data or schema incident, follow the [backup and restore guide](backup-restore.md) with a verified backup and a separate target before changing production data.
