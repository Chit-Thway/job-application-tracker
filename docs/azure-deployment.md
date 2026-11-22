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

To measure an Azure cold start, confirm the active revision has zero replicas in **Revisions and replicas**, then time one request to an Azure-backed page. Record time to first byte and compare it with the image pull and container start events in **System logs**.

## Release sequence

1. Review the change and run the checks selected by the [test strategy](test-strategy.md). GitHub Quality runs the full code checks on the pull request.
2. Merge the passing pull request into `main`.
3. If the release includes a schema change, apply its reviewed Entity Framework migration to the production database before deploying code that needs it.
4. Start the manual `Deploy production` workflow from the merged `main` commit. The workflow validates and publishes the image, then runs `--initialize-admin` with the production app's database and bootstrap admin settings before deploying the image by digest. The command checks the admin role and provisions the configured account when needed. Use `publish_only` when bootstrapping the Container App and you need the validated image before the target exists. Run the admin setup command with the new app's settings before its first web revision serves traffic.
5. Confirm `/health/live` and `/health/ready` return Healthy, the public demo loads without sign-in, and the changed user journey works.

## Public homepage preview

The [homepage performance release note](homepage-performance.md) records the production and preview timings.

The homepage Worker uses the signed-out Razor view from the same published app as the release. Export the view and its allowlisted assets from a fresh publish, then deploy the standalone preview.

```powershell
dotnet publish src/JobTracker.Web --configuration Release --output artifacts/publish
node tools/New-HomepageEdgePackage.mjs artifacts/publish
npx wrangler@4.166.0 deploy --config edge/homepage/wrangler.jsonc --env=""
```

The preview serves the homepage, images, scripts, styles, and video on its `workers.dev` hostname. Its sign-in and other application links take visitors to the production hostname. The Worker checks for the tracker session and temporary message cookies before serving the static homepage.

### Main-domain rollout

1. Bind a publicly trusted SNI certificate for `myjobtracker.com.au` in Azure and verify the certificate directly against Azure ingress using the apex hostname. Use DNS-based validation so future renewals work while Cloudflare proxies the site. The default Azure-managed apex certificate requires a direct DNS record. A publicly trusted certificate also keeps direct access working during rollback. [Azure certificate requirements](https://learn.microsoft.com/en-us/azure/container-apps/custom-domains-managed-certificates) · [Bind an existing certificate](https://learn.microsoft.com/en-us/azure/container-apps/custom-domains-certificates)
2. Set up and validate DNS-based certificate renewal using the workflow below.
3. Confirm Cloudflare's edge certificate is active and SSL/TLS is **Full (strict)**. Verify that the Azure app uses `ASPNETCORE_FORWARDEDHEADERS_ENABLED=true` so ASP.NET Core sees the original HTTPS scheme when the ingress terminates TLS. Proxy the apex A record and verify the site, sign-in, and readiness through Cloudflare before attaching the Worker. [Cloudflare TLS requirements](https://developers.cloudflare.com/ssl/origin-configuration/ssl-modes/full-strict/)
4. Use a clean checkout to export the homepage from the same publish output as the live Azure release. Run the command below with `--dry-run` to validate the package. After the proxy checks pass, run it without that flag to attach the route.
5. Confirm that the signed-out homepage responds with `Server-Timing: homepage;desc="Cloudflare"` while Azure has zero replicas. Verify sign-in, the dashboard with a session cookie, and the video. Check the application's per-IP request limits through the proxy before accepting the rollout.

```powershell
npx wrangler@4.166.0 deploy --config edge/homepage/wrangler.jsonc --env production --dry-run
```

If the Worker causes a regression, remove the `myjobtracker.com.au/*` Worker Route to restore Azure handling through the proxy. If the proxy itself causes a regression, return the apex A record to DNS only and verify both the homepage and sign-in directly.

### Azure origin certificate renewal

Restrict the GitHub `production` environment to `main`. Add the `CLOUDFLARE_ORIGIN_CERTIFICATE_DNS_TOKEN` environment secret with DNS write access for `myjobtracker.com.au` only. Set `AZURE_ORIGIN_CERTIFICATE_ID` and `AZURE_ORIGIN_ADDRESS` to the bound certificate resource ID and direct ingress address. Grant the Azure deployment identity the **Container Apps ManagedEnvironments Contributor** role scoped to that certificate resource.

Run **Renew Azure origin certificate** with **Validate only** enabled to test DNS validation against Let's Encrypt staging before the cutover. The weekly run renews the certificate when it has 30 days or less remaining and verifies that Azure serves the new certificate. Check failed runs and the origin expiry date when operating the site. GitHub can disable scheduled workflows after 60 days without activity in a public repository. [GitHub schedule behaviour](https://docs.github.com/en/actions/reference/workflows-and-actions/events-that-trigger-workflows#schedule)

## Recovery

For an application regression, deploy the last accepted image digest and verify readiness. Do not reverse a production migration as an automatic rollback. For a data or schema incident, follow the [backup and restore guide](backup-restore.md) with a verified backup and a separate target before changing production data.
