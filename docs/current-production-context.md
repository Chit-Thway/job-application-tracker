# Current production context

Last reviewed: 23 September 2026. This file gives contributors the current product and release context without publishing operator accounts or service identifiers.

## Current stage

- The application is live at [myjobtracker.com.au](https://myjobtracker.com.au).
- The public demo uses fictional data. Its guided extension walkthrough is at `/demo/extension` and does not create an account or application.
- Registration is open. New accounts verify a six-digit email code; verification emails are limited to 25 per UTC month across the application.
- New accounts start on Tier 1 with a maximum of 10 stored applications. Tier 2 is unlimited.
- Administrators can manage account access, tiers, verification, and eligible account deletion without seeing private application contents.
- The default branch is `main`. Changes reach it through reviewed pull requests.

## Hosting and configuration

The web application runs on Azure App Service, uses a separate Supabase PostgreSQL database, and sends verification and password-reset messages through Azure Communication Services Email. Production credentials and service identifiers belong in the service settings and the operator's private records, not in this repository. Local development secrets use .NET user secrets.

The domain and application behavior above are public. The operator-only service map is kept outside Git in the ignored `/private/` directory.

## Release workflow

- GitHub Quality validates code pull requests. Choose local checks using the [test strategy](test-strategy.md).
- Apply reviewed database migrations before deploying code that needs them.
- Deploy production manually from reviewed `main` with the `Deploy production` workflow.
- Verify the public demo and `/health/ready` after deployment.
