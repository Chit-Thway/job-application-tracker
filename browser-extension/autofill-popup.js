import { planAutofill } from "./autofill-matching.js";
import { accessApplicationPage } from "./autofill-page.js";

const tokenKey = "autofill:access";
const verifierKey = "autofill:verifier";
const lastFillKey = "autofill:lastFillTab";
const fill = document.getElementById("fill");
const undo = document.getElementById("undo-fill");
const status = document.getElementById("fill-status");
const connection = document.getElementById("connection");
const start = document.getElementById("start-connection");
const connect = document.getElementById("connect-form");
const code = document.getElementById("connection-code");
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

async function beginConnection() {
  const random = crypto.getRandomValues(new Uint8Array(32));
  const verifier = btoa(String.fromCharCode(...random)).replaceAll("+", "-").replaceAll("/", "_").replace(/=+$/, "");
  const hash = await crypto.subtle.digest("SHA-256", new TextEncoder().encode(verifier));
  const challenge = [...new Uint8Array(hash)].map(byte => byte.toString(16).padStart(2, "0")).join("");
  await chrome.storage.session.set({ [verifierKey]: verifier });
  const url = new URL(`${profileUrl.href}/connect`);
  url.searchParams.set("challenge", challenge);
  await chrome.tabs.create({ url: url.href });
}

start.addEventListener("click", async () => {
  start.disabled = true;
  try {
    await beginConnection();
  } catch {
    setup("Could not open the tracker. Try getting a new connection code.");
  } finally {
    start.disabled = false;
  }
});

connect.addEventListener("submit", async event => {
  event.preventDefault();
  const submit = connect.querySelector("button");
  submit.disabled = true;
  try {
    const stored = await chrome.storage.session.get(verifierKey);
    if (!stored[verifierKey]) {
      setup("Get a new connection code first.");
      return;
    }
    const response = await request("exchange", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ code: code.value.trim(), verifier: stored[verifierKey] }),
    });
    if (!response.ok) {
      setup("That code could not be used. Check it or get a new code.");
      return;
    }
    const { token } = await response.json();
    if (typeof token !== "string" || !/^[A-Za-z0-9_-]{43}$/.test(token)) throw new Error();
    await chrome.storage.session.set({ [tokenKey]: token });
    await chrome.storage.session.remove(verifierKey);
    code.value = "";
    connect.hidden = true;
    connection.open = false;
    status.textContent = "Profile connected. Open an employer application form and choose Fill.";
  } catch {
    setup("Could not connect to the tracker. Try again.");
  } finally {
    submit.disabled = false;
  }
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
      await chrome.storage.session.remove(tokenKey);
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

chrome.storage.session.get([verifierKey, lastFillKey]).then(stored => {
  connect.hidden = !stored[verifierKey];
  if (!connect.hidden) connection.open = true;
  undo.hidden = !stored[lastFillKey];
});
