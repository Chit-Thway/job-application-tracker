# Security and privacy review

Last reviewed: 23 September 2026. This is a summary of the application's controls and the checks in this repository, not a third-party security certification.

| Area | Current approach |
|---|---|
| Accounts | ASP.NET Core Identity requires a verified email address before sign-in. Verification codes expire after ten minutes, allow five failed attempts, and have a resend cooldown. The application limits verification emails to 25 per UTC month. |
| Private data | Application, company, contact, task, and history queries use the signed-in owner. Admin account tools show account metadata and counts without opening private application content. |
| Public demo | Demo records are fictional and separate from account data. The guided extension demo does not save an application or call the private capture workflow. |
| Form safety | Unsafe form requests use antiforgery validation. The public demo rejects mutation requests. |
| Job-link imports | URL imports restrict schemes, ports, destination addresses, redirects, response types, response sizes, and request time. The server does not forward user credentials to job sites. |
| Browser and caching | Production uses secure cookies, restrictive response headers, and no-store caching for authenticated responses. |
| Secrets and logs | Production settings hold connection and email configuration. Local secrets stay outside Git. Request logs omit private application text, email codes, tokens, cookies, and form values. |

Unit, integration, and real-browser tests cover these boundaries, including account isolation, verification limits, safe URL fetching, demo separation, and security headers. The [test strategy](test-strategy.md) describes when to run each group; the GitHub Quality workflow runs them on code pull requests.

Production operators should continue to review access, backups, dependency advisories, and incident procedures. The [operations guide](operations.md) and [backup and restore guide](backup-restore.md) describe those checks without publishing operator credentials.
