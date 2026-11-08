import { planAutofill } from "./autofill-matching.js";
import { accessApplicationPage } from "./autofill-page.js";

const tokenKey = "autofill:access";
const connectionResultKey = "autofill:connectionResult";
const lastFillKey = "autofill:lastFillTab";
const fill = document.getElementById("fill");
const undo = document.getElementById("undo-fill");
const status = document.getElementById("fill-status");
const connection = document.getElementById("connection");
const start = document.getElementById("start-connection");
const connectionStatus = document.getElementById("connection-status");
const connectionSummary = document.getElementById("connection-summary");
const profileUrl = new URL("/extension/profile", trackerOrigin);
document.getElementById("manage-profile").href = profileUrl.href;
document.getElementById("privacy-link").href = new URL("/extension/privacy", trackerOrigin).href;

async function activeTab() {
  const [tab] = await chrome.tabs.query({ active: true, currentWindow: true });
  if (!tab?.id || !/^https?:/i.test(tab.url ?? "")) throw new Error("Open an employer application form first.");
  if (new URL(tab.url).origin === profileUrl.origin) throw new Error("Open the employer's application form to use Fill.");
  return tab;
}

async function runInTab(tabId, operation, input) {
  const [{ result }] = await chrome.scripting.executeScript({
    target: { tabId },
    func: accessApplicationPage,
    args: [operation, input ?? {}],
  });
  return result;
}

function setup(message) {
  connection.open = true;
  status.textContent = message;
}

async function request(path, options) {
  return fetch(`${profileUrl.href}/${path}`, {
    ...options,
    cache: "no-store",
    credentials: "omit",
    redirect: "error",
    signal: AbortSignal.timeout(8000),
  });
}

async function refreshConnection() {
  const stored = await chrome.storage.session.get([tokenKey, connectionResultKey]);
  const connected = Boolean(stored[tokenKey]);
  const result = stored[connectionResultKey];
  start.hidden = connected;
  connectionSummary.textContent = connected ? "Autofill profile connected" : "Autofill profile and connection";
  if (connected) {
    connectionStatus.textContent = "Profile connected. Open an employer application form and choose Fill.";
    return;
  }
  const messages = {
    working: "Connecting to the tracker. Sign in if prompted.",
    "profile-required": "Save an autofill profile in the tracker, then try Connect again.",
    failed: "Could not complete the connection. Choose Connect to try again.",
  };
  connectionStatus.textContent = messages[result] ?? "";
  if (result) connection.open = true;
}

start.addEventListener("click", async () => {
  start.disabled = true;
  connectionStatus.textContent = "Connecting to the tracker. Sign in if prompted.";
  try {
    const started = await chrome.runtime.sendMessage({ type: "autofill:connect" });
    if (!started) throw new Error();
  } catch {
    connectionStatus.textContent = "Could not complete the connection. Choose Connect to try again.";
  } finally {
    start.disabled = false;
  }
});

chrome.storage.onChanged.addListener((changes, area) => {
  if (area === "session" && (changes[tokenKey] || changes[connectionResultKey])) void refreshConnection();
});

fill.addEventListener("click", async () => {
  fill.disabled = true;
  status.textContent = "";
  try {
    const stored = await chrome.storage.session.get(tokenKey);
    if (!stored[tokenKey]) {
      setup("Save your profile and connect the extension before using Fill.");
      return;
    }

    const tab = await activeTab();
    const scan = await runInTab(tab.id, "scan");
    const response = await request("data", { headers: { Authorization: `Bearer ${stored[tokenKey]}` } });
    if (response.status === 401 || response.status === 403) {
      await chrome.storage.session.remove([tokenKey, connectionResultKey]);
      setup("Your connection has expired or was revoked. Connect again to fill.");
      return;
    }
    if (!response.ok) throw new Error("Could not load your saved profile. Try again.");

    const plan = planAutofill(scan.fields, await response.json());
    const review = [...new Set(plan.review)].slice(0, 3).join(", ");
    const frameMessage = scan.inaccessibleFrames ? " Some embedded sections may need manual entry." : "";
    if (!plan.writes.length) {
      status.textContent = (plan.reason ?? (review ? `Review manually: ${review}.` : "No blank supported fields to fill.")) + frameMessage;
      return;
    }
    const result = await runInTab(tab.id, "write", { scanId: scan.scanId, writes: plan.writes });
    if (result.stale) {
      status.textContent = "The form changed. Review the page and try Fill again.";
      return;
    }
    const message = [`Filled ${result.filled} ${result.filled === 1 ? "field" : "fields"}.`];
    if (result.failed) message.push(`${result.failed} did not retain a value.`);
    if (review) message.push(`Review manually: ${review}.`);
    status.textContent = message.join(" ") + frameMessage;
    if (result.filled) {
      await chrome.storage.session.set({ [lastFillKey]: tab.id });
      undo.hidden = false;
    }
  } catch (error) {
    status.textContent = error instanceof Error && /^(Open|Could not load)/.test(error.message)
      ? error.message : "Could not fill this tab. Check the form and try again.";
  } finally {
    fill.disabled = false;
  }
});

undo.addEventListener("click", async () => {
  undo.disabled = true;
  try {
    const tab = await activeTab();
    const stored = await chrome.storage.session.get(lastFillKey);
    if (stored[lastFillKey] !== tab.id) {
      status.textContent = "Open the tab where you last used Fill.";
      return;
    }
    const { undone } = await runInTab(tab.id, "undo");
    status.textContent = undone ? `Undid ${undone} ${undone === 1 ? "field" : "fields"}.` : "No unchanged values from the last fill.";
    await chrome.storage.session.remove(lastFillKey);
    undo.hidden = true;
  } catch {
    status.textContent = "The previous form is unavailable for undo.";
  } finally {
    undo.disabled = false;
  }
});

void refreshConnection();
chrome.storage.session.get(lastFillKey).then(stored => {
  undo.hidden = !stored[lastFillKey];
});
