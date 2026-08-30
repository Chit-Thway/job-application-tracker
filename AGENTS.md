# Repository working rules

Start with `docs/current-production-context.md`. Do not reconstruct project history or scan the whole repository unless the current task requires it.

Use `docs/test-strategy.md` to choose the smallest relevant verification set. Documentation-only changes need `git diff --check`, not a .NET build or test run. Run the full suite only for broad, release, security, authentication, database, or cross-cutting changes.

Use the existing authenticated GitHub and Azure CLI sessions when available. Never commit passwords, tokens, connection strings, verification codes, recovery codes, or publish credentials. Store secrets only in the service designed to hold them.

Keep progress messages brief. Do not repeatedly poll unchanged CI or deployment state; report only meaningful transitions or the final result.
