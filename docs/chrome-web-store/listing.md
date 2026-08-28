# Chrome Web Store listing

## Product details

- Name: `Job Application Tracker Capture`
- Summary: `Capture the active job advertisement and open a private review draft in Job Application Tracker.`
- Category: `Productivity`
- Language: `English (UK)`
- Visibility: `Unlisted`
- Extension ID: `ofeagkadonbdgjhdiobfdnmafhoknkig`
- Store URL: `https://chromewebstore.google.com/detail/ofeagkadonbdgjhdiobfdnmafhoknkig`
- Homepage: `https://chit-thway-job-tracker-b9bpfvb5csccb5hb.australiaeast-01.azurewebsites.net/extension`
- Privacy policy: `https://chit-thway-job-tracker-b9bpfvb5csccb5hb.australiaeast-01.azurewebsites.net/extension/privacy`
- Support email: `redacted@example.invalid`

## Detailed description

Capture the job advertisement already open in your active browser tab and bring it into Job Application Tracker as an editable private review draft.

Job Application Tracker Capture helps registered tracker users avoid repetitive copying from job boards. After you click the extension, it reads the active tab's visible job details and opens the capture inside your authenticated tracker. Nothing is saved as an application until you review and confirm it.

Key features:

- Reads only the active tab after an explicit user click.
- Captures supported job metadata, visible descriptions, and the source page address.
- Recognises Schema.org JobPosting data and rendered SEEK and Indeed job fields.
- Preserves annual, hourly, daily, and weekly pay as reviewable text.
- Opens an editable, owner-scoped review draft that expires after 24 hours.
- Lets the user manually add an optional application-portal link during review; the extension does not extract or infer that link.
- Uses deterministic extraction with no external AI service.
- Contains no advertising, analytics, background browsing, or broad all-sites permission.

A verified Job Application Tracker account is required to complete and save a capture.

## Single purpose

Allow a user to explicitly capture the active tab's job advertisement and open it as an editable review draft in their private Job Application Tracker account.

## Permission justifications

- `activeTab`: Grants temporary access only to the tab where the user explicitly clicks the extension so the advertised role can be captured.
- `scripting`: Runs the packaged deterministic page reader in that active tab after the user presses the capture button. It does not load or execute remote code.
- `storage`: Remembers the configured tracker address and holds a pending capture in browser-session memory only while the clean handoff tab opens.

## Data disclosures

- Website content: Yes. The user-requested capture can include visible job-advertisement text and structured job metadata.
- Web history: Yes. The active page URL is included as the job source only when the user explicitly initiates a capture.
- Authentication information: No. The extension does not read passwords, cookies, sessions, or authentication tokens.
- Personally identifiable information: No. The extension itself does not collect the user's account identity.
- Analytics or advertising data: No.
- Data sale or unrelated profiling: No.

All handled data is necessary for the disclosed job-capture feature. It is not sold, used for advertising or credit decisions, or transferred for unrelated purposes.

## Reviewer instructions

1. Install the extension and open any public job advertisement with visible role information.
2. Click the extension toolbar action.
3. Review the prominent disclosure explaining that the current page URL and visible job content will be sent to the user's configured private tracker.
4. Expand **Advanced tracker address** only if a different HTTPS deployment or localhost is needed.
5. Press **Capture this tab and review**.
6. The extension reads the active tab only for this click, posts the capture over HTTPS, and opens a clean tracker URL containing only a short-lived single-use token.
7. Saving the draft requires a verified Job Application Tracker account; the capture UI and handoff can be inspected without granting broad host permissions.
