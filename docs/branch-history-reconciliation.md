# Branch and deployment timeline

Audit date: 12 September 2026. Dates and times below use Australia/Perth (UTC+08:00).

## What the audit found

Work was pushed to GitHub and incorporated into `main` before the recorded deployments. GitHub had 28 merged pull requests at the start of this audit. Later work also used local fast-forward merges followed by pushes, and at least one commit directly on `main`. Those changes do not all have separate GitHub pull requests or merge commits.

Two retained local branches looked unmerged because their work was included in later squash merges, which did not preserve the original branch commits as ancestors of `main`. The published autofill branch contained one new planning document, with no implementation changes.

## Original chronology

| Original date | Work | Evidence of incorporation into `main` |
|---|---|---|
| 14 August 2026 | Retention work preceding the public demo: `a401afd`, `94aae29` | [PR #25](https://github.com/Chit-Thway/job-application-tracker/pull/25), merged 20:55. The complete tree at `94aae29` matches squash commit `27fb838`. |
| 16 August 2026, 17:08 | `milestone-9-public-demo`: `6a94836` | [PR #26](https://github.com/Chit-Thway/job-application-tracker/pull/26), merged 20:44. The demo controller, catalog, models, pages and dedicated tests are present in `98c2d8f`; subsequent differences implement Milestone 10 hardening and updated milestone labels. |
| 20 August 2026, 23:57 | `extension-header-navigation`: `6f21e3a` | [PR #32](https://github.com/Chit-Thway/job-application-tracker/pull/32), merged 21 August at 00:32. Both navigation links and the regression test are present in `1a7d693`. |
| 25 August 2026 | Extension handoff/workflow updates: `506b37c`; application quick links: `e1f2e85` | Local history records a fast-forward merge for the first change and a direct `main` commit for the second. Both were pushed and deployed from `main`. |
| 28–29 August 2026 | Account tiers: `446dd5d`; registration: `bec5345`; account deletion and registration refinements: `c94e7cc` | All are ancestors of the pushed `main`. Local history records fast-forward merges for the registration and follow-up branches. |
| 31 August 2026, 23:22 | Consistent page headings: `dc3ad76` | Local fast-forward merge into `main`, followed by a push and a successful [deployment](https://github.com/Chit-Thway/job-application-tracker/actions/runs/33408111945). |
| 2 September 2026, 15:29 | Homepage and application search polish: `b220941` | Local fast-forward merge into `main`, followed by a push and a successful [deployment](https://github.com/Chit-Thway/job-application-tracker/actions/runs/33603967021). |
| 2 September 2026, 21:02 | Prosple and Greenhouse capture fixes: `ea6c2cf` | Explicit merge commit `7d1739d` on `main`, pushed at 21:03, followed by a successful [deployment](https://github.com/Chit-Thway/job-application-tracker/actions/runs/33633454400). |
| 12 September 2026, 02:45 | `chris/extension-autofill-upgrade`: `a0a36ec` | Published at 02:46 with the [autofill plan](extension-autofill-plan.md); unmerged at the start of this audit. |

## Reconciliation

The reconciliation retains the original branch tips and commit dates, and adds separate merges in this order:

1. `milestone-9-public-demo` — record the original ancestry while retaining the current `main` tree, since its implementation already arrived through PRs #25 and #26.
2. `extension-header-navigation` — record the original ancestry while retaining the current `main` tree, since its implementation already arrived through PR #32.
3. `chris/extension-autofill-upgrade` — merge the new planning document normally.

The two historical merges deliberately use Git's `ours` strategy after comparing the original contributions against their incorporating commits. Each merge was checked to have exactly the same tree as its first parent. This avoids reintroducing old code, migrations or interfaces. New merge timestamps reflect the actual reconciliation date; historical dates are not rewritten.

The resulting pull request changes documentation and ignore rules only. Application source, extension source, tests, dependencies and deployment workflows remain identical to `7d1739d`. The latest recorded production deployment remains the successful 2 September run from that commit; this reconciliation does not deploy a new release.

## Large local change count

The audit found 7,324 untracked files under `.tmp/linkedin-project-deck`, one generated extension ZIP, and one existing modification to `AGENTS.md`. The temporary directory includes presentation outputs and installed dependencies. These generated files are now covered by `.gitignore` and remain on disk. The existing `AGENTS.md` edit is preserved locally and is outside this reconciliation commit.

Validation follows the [test selection strategy](test-strategy.md): check the final diff for whitespace errors, verify changed links, verify historical ancestry and tree equality, and confirm that no application code changes are introduced.
