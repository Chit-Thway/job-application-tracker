# Job Application Tracker Capture

This Chrome Manifest V3 extension captures the active job advertisement, including its readable job description, and opens an editable review draft in Job Application Tracker.

## Install from the Chrome Web Store

Open the extension page inside Job Application Tracker and choose **Install from Chrome Web Store**. Chrome shows the extension's permissions before installation and delivers approved updates automatically.

## Install for local development

1. Start Job Application Tracker and sign in.
2. Open `chrome://extensions` in Chrome or `edge://extensions` in Edge.
3. Turn on **Developer mode**.
4. Choose **Load unpacked** and select this `browser-extension` directory.
5. Pin **Job Application Tracker Capture** to the browser toolbar.

After pulling an extension update, use the extension page's **Reload** button before retesting an already open job tab.

The published extension defaults to the production HTTPS tracker. Expand **Advanced tracker address** in the popup only when testing a different deployment or localhost.

## Use

1. Open a public job advertisement in the active tab.
2. Click the extension.
3. Choose **Capture and review**.
4. Review and correct the private 24-hour draft in the tracker.
5. Confirm only when the details are right.

If the tracker asks you to sign in, sign in, return to the job advertisement, and click the extension again.

## Privacy and permissions

- `activeTab` grants temporary access only to the tab where the user clicked the extension.
- `scripting` runs the deterministic page reader after that click.
- `storage` remembers the tracker address locally in the browser.
- Before each capture, the popup explains that the page URL and visible job content will be sent over HTTPS to the user's private tracker for review.
- There are no broad host permissions, content scripts, background workers, analytics, remote APIs, or AI calls.
- The captured payload travels in a URL fragment, which is not sent in the initial HTTP request. The tracker removes the fragment before posting the payload through its authenticated, anti-forgery-protected form.
- A capture creates only a review draft. It never saves an application automatically.

The page reader supports official Schema.org `JobPosting` metadata, SEEK's rendered job fields, Indeed's selected job-detail panel, and conservative generic fallbacks. Description headings, paragraphs, and list items are preserved as plain text for safe formatting by the tracker. Pay remains reviewable text, so annual, hourly, daily, and weekly rates can be preserved as advertised.
