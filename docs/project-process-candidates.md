# Project process candidates

## 01 — Problem

**Title:**\
Lost track in a phone interview

**Description:**\
After applying for several IT jobs, I received a phone call about a role but could not remember the company or when I had applied. I wanted to stop that happening again.

**Evidence:**\
User-provided project story. The repository supports tracking application details, dates and status history.

**Relevant link:**\
[README.md](../README.md)

## 02 — Solution

**Title:**\
One place for every application

**Description:**\
I built a private workspace that keeps applications, status history, contacts, notes, tasks and appointments together, while showing the next action.

**Evidence:**\
`README.md`, “Complete tracking workflow” and Product direction.

**Relevant link:**\
[DashboardService.cs](../src/JobTracker.Web/Applications/DashboardService.cs)

## 03 — Problem

**Title:**\
Imports were inconsistent

**Description:**\
Copying and pasting job details was tiring, while job links and page layouts varied too much between job boards for importing to be reliable enough on their own.

**Evidence:**\
User-provided project history; `README.md`, pasted-text and public-URL extraction sections.

**Relevant link:**\
[JobPostingUrlImportService.cs](../src/JobTracker.Web/Extraction/JobPostingUrlImportService.cs)

## 04 — Solution

**Title:**\
★ Capture from the active tab

**Description:**\
I built the browser extension from the original idea: it reads the current job-ad tab only after a click, then opens the details as a private review draft.

**Evidence:**\
`README.md`, “Browser extension capture”; commits `5e744a3` and `506b37c`.

**Relevant link:**\
[browser-extension/README.md](../browser-extension/README.md)

## 05 — Problem

**Title:**\
Manage who can use the tracker

**Description:**\
Once accounts could be created, I needed a way to control access and manage users without opening their private job-search records.

**Evidence:**\
Commit `1a7d693`; `README.md`, open registration and account administration sections.

**Relevant link:**\
[AdminManagementService.cs](../src/JobTracker.Web/Admin/AdminManagementService.cs)

## 06 — Solution

**Title:**\
Create an admin-only area

**Description:**\
I created an admin page where only administrators can manage users, account tiers, verification, access locks and account deletion. The idea was inspired by an admin page I worked on with my supervisor Chris for Accessory Archive.

**Evidence:**\
`AdminController.cs`; `AdminManagementService.cs`; user-provided inspiration context.

**Relevant link:**\
[Accessory Archive](https://accessory-archive.dev4.concise.digital/)

## 07 — Problem

**Title:**\
A fixed ghosting rule was too rigid

**Description:**\
The original plan used a fixed 30-day ghosting rule. Later, I wanted the timing to be adjustable in Settings so the tracker could better fit different job-search situations.

**Evidence:**\
User-provided project history. The repository confirms a 30-day ghosting prompt and configurable Settings, but not the configurable ghosting threshold.

**Relevant link:**\
[Settings.cshtml](../src/JobTracker.Web/Views/Home/Settings.cshtml)

<!-- Verification note: Current code requires a user to confirm Ghosted after 30 days. Settings currently configures retention and deletion warnings, not ghosting days. Use this node only if you can verify the later ghosting-settings version elsewhere. -->

## 08 — Solution

**Title:**\
Give users more control

**Description:**\
Rather than relying only on a fixed rule, the tracker evolved toward user-controlled timing and settings for how old applications are handled.

**Evidence:**\
User-provided project history; `RetentionOperationsService.cs` supports configurable retention and warning periods.

**Relevant link:**\
[RetentionOperationsService.cs](../src/JobTracker.Web/Applications/RetentionOperationsService.cs)

## 09 — Result

**Title:**\
A complete and mature tracker

**Description:**\
The project grew into a private job-search workspace with capture tools, application history, reminders, admin controls, verified accounts, secure handoff, browser tests, accessibility checks and operational documentation.

**Evidence:**\
`README.md`, current status and Product direction; `docs/security-review.md`.

**Relevant link:**\
[README.md](../README.md)

## 10 — Lesson

**Title:**\
A job search is more than applications

**Description:**\
**Possible inference:** Building the project showed that a job search also includes follow-ups, conversations, interviews, waiting for replies and knowing what to do next.

**Evidence:**\
`README.md`, “Complete tracking workflow”.

**Relevant link:**\
[README.md](../README.md#complete-tracking-workflow)

# Suggested final flow

01 → 02 → 03 → 04 → 05 → 06 → 07 → 08 → 09 → 10

# Possible extras

- Keep node 04 visually prominent; it connects the inconsistent-import problem directly to the original browser-extension idea.
- Keep the ghosting pair only after confirming the configurable ghosting-days version exists outside the current repository.
