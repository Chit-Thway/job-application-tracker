# Test selection strategy

The suite includes unit, integration, and Playwright browser tests. Test age does not make a test obsolete: migration and early-feature tests still protect the schema and behavior running in production. Select local checks by risk; the full suite remains the release gate.

## Verification levels

| Change | Default verification |
|---|---|
| Markdown, comments, repository metadata, or link-only documentation | `git diff --check`; check only changed links; no .NET build or tests |
| Isolated pure logic | Relevant unit-test class, then build the affected project if needed |
| One application feature or page | Relevant unit and integration test classes only |
| Authentication, authorization, account deletion, tier limits, secrets, SSRF/network safety, or owner isolation | Relevant security-sensitive unit and integration groups |
| Database model or migration | Relevant migration-contract and integration tests; inspect generated migration |
| Shared middleware, dependency upgrades, broad refactors, production release, or uncertain impact | Full suite |
| Browser layout/navigation/copy behavior | The affected Playwright journey; all browser journeys for broad UI or release validation |
| Performance-sensitive query or dashboard work | `PersonalUsePerformanceTests`; otherwise skip it |

## Current test groups

### Production-critical for sensitive changes

- `AuthenticationJourneyTests`
- `AdminManagementTests`
- `OwnerIsolationTests`
- `HardeningTests`
- `ApplicationServiceTests` for Tier 1/Tier 2 limits
- `ExtensionCaptureHandoffServiceTests` and `BrowserExtensionWorkflowTests` for the encrypted one-time extension handoff
- `PublicUrlSafetyPolicyTests`, `SafeHttpConnectionFactoryTests`, and `SafeJobPostingFetcherTests` for URL-import network safety

Run only the classes related to the changed sensitive surface. Do not run this whole list for ordinary copy or layout work.

### Feature-specific; ignore unless that feature changed

- Application CRUD/workflow/bulk changes: `ApplicationWorkflow*`, `ApplicationServiceTests`, `BulkApplication*`
- Dashboard and action centre: `Dashboard*`
- Retention and ghosting: `Retention*`
- Pasted/URL extraction: `Pasted*`, `JobPostingHtmlExtractorTests`, `UrlExtractionWorkflowTests`, `ExtractionDraftServiceTests`
- Browser extension: `BrowserExtension*`, `ExtensionCaptureHandoff*`
- Public demo: `DemoCatalogTests`, `PublicDemoTests`
- Email content: `AccountEmailTemplateTests`
- Company matching: `CompanyNameMatcherTests`
- Status simplification: `ApplicationStatusSimplificationTests`

### Conditional; normally skip during routine work

- `PersonalUsePerformanceTests`: run for dashboard/query performance work and releases.
- `CriticalJourneysTests`: run the affected browser journey for UI behavior; run all five for broad UI, authentication-flow, or release changes.
- `*MigrationContractTests`: run only when database models, migrations, account data, retention, portal URLs, or extension handoff storage change.
- Static asset checks in `FoundationApplicationTests`: run when shared layout, middleware, routing, headers, or those assets change.

No current test class is marked for deletion. A test should be removed only when its production behavior has intentionally been retired and the application code/data migration has also removed that behavior.

## Targeted commands

Replace `ClassName` with one or more relevant test class names:

```powershell
dotnet test tests/JobTracker.UnitTests --filter "FullyQualifiedName~ClassName"
dotnet test tests/JobTracker.IntegrationTests --filter "FullyQualifiedName~ClassName"
dotnet test tests/JobTracker.BrowserTests --filter "FullyQualifiedName~TestMethodName"
```

Run the full suite only when the table above calls for it:

```powershell
dotnet test JobTracker.sln
```
