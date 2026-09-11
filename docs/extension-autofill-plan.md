# Extension autofill upgrade: plan and milestones

- Date: 12 September 2026
- Status: planning only; implementation has not started
- Branch: `chris/extension-autofill-upgrade`

## Outcome

Extend My Job Tracker so a user can upload a resume, review an extracted applicant profile, complete missing information, and click **Fill form** in the browser extension to populate an open job application. Make name and address handling reliable across different form layouts.

This is useful for personal job applications and as an employer-review project. Success means demonstrable engineering quality, a usable experience, and honest evidence about coverage and limitations. Commercial positioning can be reconsidered later without changing the core feature.

The implementation will follow the user's requirements and the project's own design. The user's observation that Simplify tracks a job when autofill starts is competitive context, not a requirement to reproduce its implementation or to mark an unfinished application as submitted.

## User experience

1. The website header links to **Extension**, replacing **Install extension**. The page retains installation instructions and provides a signed-in **Autofill profile** area.
2. The user uploads a resume or starts the profile manually. Import produces editable suggestions. The user checks them and saves the profile.
3. Missing information, including address components and referees, can be added or corrected at any time.
4. The user connects the extension to their account and opens an application form.
5. **Fill form**, directly below **Capture this tab and review**, fills suitable visible, enabled fields on the current page. It reports what was filled and what needs attention.
6. The user reviews the result, completes missing fields, opens the next section if necessary, and clicks Fill form again. Submission remains the user's action.

Replace the entire red-box area in the supplied popup screenshot with this short reminder:

> Make sure the full job description is visible. Click "See more" before capturing.

The published extension always connects to `https://myjobtracker.com.au`. **Advanced tracker address** appears only in the development package. The production package must ignore old stored tracker-address overrides, not merely hide the input.

The compact popup retains its privacy link. Installation, connection and upload screens will explain the relevant data use without restoring the removed explanatory panels.

## Existing foundation

The inspected source has a Manifest V3 extension using `activeTab`, `scripting`, and `storage`. Capture injects a page reader, stages a payload in session storage, and opens an encrypted, single-use handoff to an editable tracker draft. Applicant autofill, a reusable applicant profile and resume parsing do not exist yet.

The website uses ASP.NET Core, Identity and EF Core. Existing owned records use `OwnerId` and account-deletion relationships. Reuse those conventions for applicant data. The current Identity display name and login email must remain separate from a person's application name and preferred contact email.

Implementation starts with these surfaces and their direct dependencies:

| Surface | Existing entry points | Planned change |
|---|---|---|
| Extension | `browser-extension/popup.html`, `popup.js`, `popup.css`, `manifest.json`, `handoff.js` | Two actions; production/development packaging; field discovery, matching and filling modules |
| Website navigation | `src/JobTracker.Web/Views/Shared/_Layout.cshtml` | Rename the header link |
| Extension area | `src/JobTracker.Web/Views/Home/BrowserExtension.cshtml` and `Controllers/HomeController.cs` | Installation plus an authenticated profile workflow |
| Applicant data | `src/JobTracker.Web/Data/ApplicationDbContext.cs` and existing ownership conventions | Separate applicant profile, repeated entries and connection records |
| Capture | `src/JobTracker.Web/Extraction/` and `Controllers/ApplicationImportsController.cs` | Preserve current handoff and improve incomplete-description feedback |
| Verification | Existing unit, integration and Playwright projects | Add behavioral coverage using synthetic resumes and forms |

See [current production context](current-production-context.md), [extension documentation](../browser-extension/README.md), and [test selection strategy](test-strategy.md).

## Profile and extraction decisions

### Profile structure

Store these as explicit, editable fields rather than treating the resume as a single block of text:

- Name: full display form, given names, family name, and optional preferred name. Do not assume every name can safely be split on its final space; allow a single name and manual correction.
- Contact: application email, phone with country code, and personal links such as LinkedIn, GitHub and portfolio.
- Address: unit/subpremise, street line, additional address line, suburb/locality, state/region, postcode as text, and country. Retain the user's original address for reference.
- Work experience: employer, title, location, start/end month and year, current-role flag, and description.
- Education: institution, qualification, field of study, dates, and current-study flag.
- Skills, certifications and projects when present; importing a section does not imply every destination form supports it.
- Referees: name, relationship, organisation, role, email and phone, added or confirmed manually.
- Optional application answers entered explicitly by the user, such as work rights or availability. Sensitive declarations and consent choices remain manual in the initial release.

Applicant profile creation does not consume an application slot. Existing account tiers continue to govern stored job applications. Employer contact records in the tracker must not be repurposed as applicant referees.

### Resume input

Initial target formats are **PDF with a text layer and DOCX**, with a proposed 5 MB limit. Prefer simple layouts for best results. A preliminary read of the supplied `ITResume.pdf` found one page with extractable text; this establishes input feasibility, not parsing accuracy.

Use server-side document parsing in the existing application. Evaluate [PdfPig](https://github.com/UglyToad/PdfPig) for PDF text and reading order, and [Open XML SDK](https://github.com/dotnet/Open-XML-SDK) for DOCX paragraphs and tables. Confirm maintenance, licensing, dependencies and resource limits before selecting pinned versions.

The first release uses deterministic extraction with source evidence and explicit uncertainty. Keep the parser behind a small interface so another extraction approach can be evaluated later using the same benchmark. OCR, legacy DOC, password-protected files, image-only resumes and generative rewriting are later options; unsupported files receive a clear explanation and a manual-entry path.

An import creates a draft, never silently replaces a saved profile. Show suggested values next to the existing values when importing again. Preserve manually confirmed fields, detect repeated entries, and let the user choose replacements. Never invent an address component, qualification, date, referee, or missing answer.

Uploaded files are processed with file-signature checks, bounded text/page/decompression limits, a timeout and cleanup on success or failure. Disable external resource loading. Do not retain the original file by default; temporary extraction evidence expires after review or abandonment. The saved profile contains confirmed information. Resume attachment to employer forms is a separate later feature because it requires retaining or selecting the original file.

## Connection and filling design

### Secure profile access

The tracker is the profile source of truth. The extension retrieves the current profile through a dedicated authenticated connection; the existing anonymous inbound capture handoff must not become a way to retrieve private applicant data.

The first connection prototype will prove the complete browser flow before the final interface is built:

- Start connection from the extension and open a sign-in/connection page on the configured tracker origin.
- Bind the exchange to the requesting extension, an unpredictable state value and proof of possession. A short-lived, single-use pairing exchange yields revocable, narrowly scoped profile-read access.
- Validate the account and connection server-side on profile access, including lock, deletion, revocation and expiry. Check access again after website sign-out; cached personal data must not continue to fill forms after access is rejected.
- Keep tokens in trusted extension session storage and out of employer pages, query strings, logs and source control. A browser restart or expiry may require reconnection in the first release.
- Fetch a fresh profile for a fill action. On connection failure, clear sensitive cached data and explain how to reconnect. Switching accounts must not expose or reuse the previous user's profile.

Use `activeTab` for user-triggered access to application pages. If the connection requires host access, limit the production package to the exact tracker origin, with separate development settings. Chrome documents the lifetime of [active-tab access](https://developer.chrome.com/docs/extensions/develop/concepts/activeTab) and the host permissions needed for [extension network requests](https://developer.chrome.com/docs/extensions/develop/concepts/network-requests). Do not assume access persists after navigation to another origin or reaches every embedded form.

Resolve matching in the trusted extension context. Pass only the values needed for a particular fill operation into the target page, without tokens or the complete resume/profile. Disclosures must make clear that values written into a website's fields can be read by that website before submission.

### Field matching and writing

Separate discovery, semantic matching, value formatting, field writing and verification. Prefer semantic evidence: autocomplete attributes, associated labels, accessible names, section headings and fieldsets; then use field names, IDs and placeholders as weaker clues. Avoid matching a field on one vague word such as "name" or "address".

Scope each field to the relevant person and section. Applicant details must not leak into employer, emergency-contact, referee, login or search fields. Only fill matches above an explicit confidence threshold, and explain skipped cases in plain language.

| Form variation | Expected behavior |
|---|---|
| Full name versus given/family names | Use the corresponding confirmed profile values; do not resplit a confirmed name |
| Single-name applicant | Preserve their chosen name representation; do not fabricate a family name to satisfy validation |
| One full-address field | Compose the confirmed components in the configured country's order |
| Street address plus suburb/state/postcode | Fill each component once; keep locality and postcode out of the street-only field |
| Address line 1 and optional line 2 | Use the destination's labels for unit/street allocation; leave missing line 2 blank |
| Unit numbers and PO boxes | Preserve the value and address type; do not force a PO box into a street-number pattern |
| State/country dropdowns | Match known labels and codes, such as WA/Western Australia, using country context |
| Postcodes such as 0800 | Preserve leading zeroes; treat them as text |
| Address autocomplete suggestions | Choose an unambiguous matching suggestion through the supported control; otherwise leave it for review |
| Phone with separate country selector | Separate country code and national number without duplicating the code |
| Month/year versus exact-date fields | Preserve the available precision; do not invent a day |
| Repeated jobs, education or referees | Match the correct group; fill entries already opened by the user |
| Custom controls and dynamically rendered fields | Use bounded, tested interaction strategies and verify the form retained each value |

Fill blank fields by default. Keep user-entered values. Handle native selects, text inputs and textareas first, then tested custom dropdowns and controls. Trigger the events required by the form and verify the actual retained value, rather than equating a DOM assignment with success.

Skip hidden, disabled, read-only, password, payment and verification-code fields. Do not click Submit, Next, legal declarations or consent checkboxes. Radio questions are supported only when their meaning and the user's explicitly stored answer are unambiguous and the question is within the supported scope.

Bound each run to the chosen tab, frame and current form. Stop if navigation or major form changes invalidate the selection. Same-origin frames and accessible shadow roots are tested explicitly; inaccessible frames and closed controls produce a limitation message. Repeated clicks must be safe and must not duplicate entries.

Show a result such as **"Filled 12 fields. 3 need your attention."** Distinguish missing profile information, uncertain matches and unsupported controls. Offer an undo of values changed in that run only if they still match the extension's last written value; preserve subsequent user edits.

## Milestones

Each milestone ends with a working demonstration and the relevant checks. The first complete personal-use version is reached after M5. M6 establishes named-site coverage. M7 makes the work easy for an employer to assess and prepares a release without requiring a commercial decision.

### M1 - Popup and environment foundation

**Deliver:** Replace the red-box text, rename the website header link, and produce clearly separated production and development extension packages. Establish the two-button layout; show Fill form with a setup explanation until the feature is available. Do not publish an inert control as a finished feature.

Improve capture feedback alongside the reminder. Detect supported collapsed/truncated descriptions and stop with an instruction to expand them. Do not silently truncate at character limits or label a partial extract as complete. Where generic pages provide no reliable completeness signal, surface uncertainty in the review draft. A short reminder alone cannot guarantee full extraction on arbitrary sites.

**Done when:** Production always uses the canonical domain, including when upgrading an installation with a saved custom address; development retains the address setting. Existing capture-to-review still works. Collapsed, expanded, selected-job and over-limit fixtures behave as specified.

**Verify:** Target affected layout/capture journeys and manifest/import tests. Inspect the actual generated packages, including their destination settings. No database changes are needed here.

### M2 - Editable applicant profile

**Deliver:** Add the signed-in profile area, structured contact/address fields, repeated experience/education/referee entries, save validation and clear missing-field indicators. Manual setup works without a resume. Add only the necessary owned records and migration.

**Done when:** A user can create, edit and delete their profile. Another account cannot read or update it. Account deletion removes the new records, and public/demo routes expose no personal profiles. Application email and name can differ from login details. Repeated entries preserve their order and date precision.

**Verify:** Profile validation and ownership integration tests, relevant migration contracts, account-deletion tests and the affected profile browser journey. Keep account tier behavior unchanged.

### M3 - Resume import and review

**Deliver:** PDF/DOCX upload, bounded text extraction, draft profile suggestions, source evidence, confidence/needs-review indicators and repeat-import merging. Missing items remain editable.

**Done when:** Synthetic single-column, multi-column and table-based resumes exercise different reading orders. Unsupported/corrupt/image-only/password-protected inputs fail helpfully. Reimport preserves confirmed edits and avoids duplicates. Abandoned uploads and evidence expire. Test the user's supplied resume locally with no committed copy, extracted personal data or personal screenshots.

**Verify:** Parser accuracy against hand-labelled expected fields; invalid-file/resource-limit tests; authenticated upload, draft ownership, merge and cleanup tests. Record precision and recall separately for contact details and repeated sections. New parsing dependencies trigger the full-suite verification required by the repository strategy.

### M4 - Connect the extension to the profile

**Deliver:** Prove and then implement account connection, scoped profile retrieval, disconnect and reconnect behavior. Document the exchange and data lifetime. Add only the extension permissions and background behavior the chosen design actually needs.

**Done when:** The correct account's current profile is available to Fill form. Wrong-origin, spoofed-client, expired/replayed pairing, revoked connection, sign-out, account switch, locked-account and deleted-account cases cannot retrieve or reuse profile data. No credentials or full profile are exposed in page messages or URLs.

**Verify:** Integration tests for the new exchange and account checks, plus a browser journey using a loaded extension. Update existing manifest assertions to verify the intended new permission boundary, rather than blindly preserving assumptions that no longer apply. Recheck the current capture handoff only where shared behavior changes.

### M5 - Reliable autofill, with names and addresses first

**Deliver:** The discovery/matching/writing engine, the address and name rules above, basic repeated-section support, retained-value checks, result summary and safe undo. Use controlled synthetic forms to make failures reproducible.

**Done when:** One-line and split-address examples, unit/PO-box variations, alternate labels, leading-zero postcodes, single names, country/state controls, prefilled fields and repeated people all have behavioral tests. Clicking twice does not overwrite user edits or duplicate records. User submission remains manual. Unsupported and uncertain cases are reported instead of guessed.

**Verify:** Run the actual injected JavaScript in Playwright pages, including framework-controlled inputs, dynamic content and frame boundaries. Add a persistent browser context for loaded-extension journeys; ordinary website-only tests do not prove extension behavior. Do not rely solely on checking whether source files contain expected strings.

### M6 - Portal compatibility and evidence

**Deliver:** Validate the engine against a small published support matrix. First candidates: SEEK's application flow and Greenhouse; then Lever and a bounded Workday journey. Check each actual flow before claiming support. Treat each portal adapter as a separate increment and document unsupported field types.

**Done when:** Every advertised combination of site, form section and control type has recorded evidence. SEEK's own application and an external portal reached from SEEK are reported separately. Page changes fail visibly. A user can recover through manual entry without losing what the extension filled successfully.

**Verify:** Use synthetic fixtures for repeatable automation and pre-submission live checks where access permits. Do not submit applications, create employer accounts or send real applicant data solely for testing. Where access is unavailable, mark coverage unverified rather than substituting a claim based on a similar page.

Freeze a representative benchmark before tuning adapters. Initial release targets are at least **98% correct writes** and **90% completion of eligible, supported blank fields** on that benchmark, with every address/name regression case passing. Report the number of forms and fields, skipped fields, wrong writes and time to fill. These are future targets, not measured results or promises of universal accuracy. Keep live-site observations separate from synthetic benchmark results.

### M7 - Employer demonstration and release preparation

**Deliver:** A safe, resettable demonstration using a synthetic resume and several contrasting forms: a simple application, a split Australian address, and a dynamic form with an ambiguous field. Build on the existing demo approach with fake data isolated from real profiles. Include an accessible profile flow, keyboard behavior, loading/error states and a short walkthrough.

Provide a concise architecture explanation, support matrix, benchmark results, sample screenshots/video and known limitations. Explain why the engine sometimes leaves a field blank and how account isolation is enforced. Present the project as independently built application software, with claims backed by the demonstrated behavior.

Prepare production/development packages, updated store text and privacy disclosures, versioning, migration notes and a rollback path. Update statements such as "no background workers" or "no host permissions" if the final connection design changes them. The [Chrome disclosure requirements](https://developer.chrome.com/docs/webstore/program-policies/disclosure-requirements) also apply when an extension introduces new user-data practices.

**Done when:** An employer can follow the documented demo without using the developer's resume or personal account. The benchmark and limitations are reproducible. The production package connects only to the live domain. The prior capture workflow and account protections still pass release checks.

**Verify:** Full suite for a release, all affected browser journeys, package inspection and a final capture/profile/fill smoke test. The web application must support the new extension before its store update is submitted. Plan an additive schema migration and a feature-disable path; do not rely on a destructive downgrade for rollback. GitHub branch publication, production deployment and Chrome Web Store publication are separate steps. This planning task publishes only the branch/document.

## Acceptance and scope controls

Mandatory release gates:

- Zero cross-account profile disclosures in the security tests.
- Zero automatic submissions, accidental consent selections or overwrites of user-entered data in the guardrail suite.
- Every declared address/name regression case passes, including ambiguity cases that should remain unfilled.
- No silent truncation in supported description-capture cases; unknown completeness is made explicit.
- Actual stored form values and correct section assignment are verified.
- Supported input formats, controls and sites match the published evidence.
- Personal resumes, extracted data, tokens and credentials are absent from commits and demonstration artifacts.

M1-M5 are the core upgrade. A narrow but well-tested M6 is preferable to naming many unverified platforms. M7 provides a clear employer-review completion point regardless of whether the product is monetised.

## Optional follow-on: save a job when autofill starts

Keep this separately selectable after the core autofill is working. It could reduce steps in the user's workflow, but it changes what the tracker records and is not required to finish M1-M7.

If selected later, starting autofill may create or update a **Saved / Application started** record using a canonical job URL or reference to avoid duplicates. The current pipeline begins at **Applied**, so this needs an explicit stage/data-model decision and appropriate migration/reporting tests. Filling a form is not evidence that it was submitted.

Preserve description completeness checks, respect application limits and owner isolation, and decide explicitly how duplicates across SEEK and an employer portal are linked. Automatic promotion to Applied would require a separately verified submission signal; otherwise the user confirms submission. Automatic interview/rejection updates, mass application submission, AI-generated answers, OCR and resume-file attachment are later features with their own scope.

## Resuming the work

Start at M1 after reviewing the current checkout and this plan. Reuse [the test strategy](test-strategy.md) to select checks for each change; widen verification only for new edits, failures, unresolved risk or its full-suite triggers. Keep milestones in reviewable commits and record evidence when each exit condition is met.

No implementation milestone is complete merely because this plan exists. There are no schedule commitments or background tasks: the user will decide when to resume.
