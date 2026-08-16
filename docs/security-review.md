# Milestone 10 security review

Reviewed 16 August 2026 against the application code and the current [OWASP HTTP Headers guidance](https://cheatsheetseries.owasp.org/cheatsheets/HTTP_Headers_Cheat_Sheet.html), [OWASP Content Security Policy guidance](https://cheatsheetseries.owasp.org/cheatsheets/Content_Security_Policy_Cheat_Sheet.html), and [.NET NuGet auditing guidance](https://learn.microsoft.com/nuget/concepts/auditing-packages).

## Scope and conclusion

The review covered authentication, authorization, form submissions, output encoding, public URL fetching, errors, secrets, caching, response headers, diagnostics, dependencies, and public-demo separation. No known high- or critical-severity application vulnerability remains at this milestone. Deployment-specific controls listed under **Milestone 11 gates** must still be completed before public hosting.

## Threat boundaries and controls

| Area | Evidence and decision |
|---|---|
| Authentication | ASP.NET Core Identity requires verified unique email, a 12-character complex password, five-attempt lockout, generic login/recovery responses, and one-time invitation registration. Authentication cookies are HTTP-only, SameSite Lax, eight hours, and Secure-only outside Development/Testing. |
| Authorization | Private controllers require authentication. Data services derive the owner from the server-side principal and apply owner filters before reads or writes. Composite database relationships reject cross-owner links. |
| Cross-site requests | MVC applies automatic antiforgery validation to unsafe methods. The antiforgery cookie is HTTP-only, SameSite Strict, and Secure-only outside Development/Testing. Demo mutation methods deliberately return 405 and never enter private services. |
| Output and browser isolation | Razor encodes user text. Stored job descriptions are formatted from text rather than executed HTML. CSP restricts resources and forms to self, disables objects and framing, and is paired with `nosniff`, frame denial, no-referrer, Permissions Policy, COOP, CORP, and cross-domain-policy denial. |
| SSRF and URL imports | Only public HTTP(S) destinations and default ports are accepted. DNS and the connected socket are checked; private, loopback, link-local, metadata, documentation, multicast, transition, and credential-bearing destinations are rejected. Redirects, response type, decompressed size, and time are bounded. Cookies, proxies, credentials, authorization, referrer, and activity propagation are disabled. |
| Secrets | Local database and bootstrap credentials use .NET user secrets. No connection string, invitation code, password, email token, or private record is committed. Production configuration must use host-managed settings. |
| Errors and logs | Production uses the exception handler and HSTS. Request diagnostics log only HTTP method, endpoint template/display name, status, duration, and generated request ID; they do not log path values, query strings, bodies, cookies, email, user ID, role titles, or source text. Health responses omit exceptions and connection details. |
| Caching | Authenticated responses are explicitly `no-store`; account pages already disable caching. The public demo has a non-personalized layout, deterministic synthetic data, five-minute public caching, and tests proving private canary data cannot appear. |
| Dependencies | `dotnet list JobTracker.sln package --vulnerable --include-transitive` reported no known vulnerable package on 16 August 2026. Microsoft runtime packages and AngleSharp were updated to current compatible releases. Major test-tool upgrades were not mixed into the launch branch because they have no reported vulnerability and require a separate compatibility review. |

## Automated evidence

- Unit and integration tests cover owner isolation, authentication and recovery, antiforgery-backed journeys, invitation concurrency, SSRF policy and connected-IP checks, extraction limits, retention concurrency, demo privacy, security headers, no-store private caching, and minimal health output.
- Playwright drives login, manual creation, Saved state, status history, dashboard navigation, and the anonymous read-only demo in real Chromium.
- The CI workflow restores locked packages, checks current advisories, verifies formatting, builds with warnings as errors, installs pinned Chromium, runs every test project, and validates publish output.

## Milestone 11 gates

These are explicit deployment prerequisites, not silently accepted risks:

1. Persist and protect ASP.NET Core Data Protection keys in an Azure-supported shared store before scaling beyond one disposable instance.
2. Replace the development in-memory email sender with a production provider and verify SPF/DKIM/DMARC for its sending domain.
3. Configure trusted forwarded headers only after the exact Azure proxy topology is known; never trust arbitrary forwarders.
4. Store connection strings and provider secrets in Azure-managed settings, rotate the development database password before launch, and verify no secret exists in deployment logs.
5. Run the backup/restore rehearsal in `docs/backup-restore.md` against a disposable non-production Supabase project.
6. Run the manual accessibility checklist in `docs/accessibility.md` on the deployed candidate.

## Reporting a suspected incident

Do not paste secrets or private application text into GitHub. Record the time, affected route, request ID, and sanitized symptom, then follow `docs/operations.md` to contain, rotate, restore, and verify.
