import { planAutofill } from "./autofill-matching.js";
import { accessApplicationPage } from "./autofill-page.js";
import { enableDocumentPicker } from "./autofill-documents.js";

const tokenKey = "autofill:access";
const lastFillKey = "autofill:lastFillTab";
const fill = document.getElementById("fill");
const undo = document.getElementById("undo-fill");
const status = document.getElementById("fill-status");
const connection = document.getElementById("connection");
const connectionSummary = document.getElementById("connection-summary");
const reviewPanel = document.getElementById("fill-review");
const answerSuggestions = document.getElementById("answer-suggestions");
const unfilled = document.getElementById("unfilled-fields");
const refereePanel = document.getElementById("referee-picker");
const refereeSelect = document.getElementById("referee-select");
const refereeFields = document.getElementById("referee-fields");
const refereeStatus = document.getElementById("referee-status");
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

function requestProfile(token) {
  return fetch(`${profileUrl.href}/data`, {
    headers: { Authorization: `Bearer ${token}` },
    cache: "no-store",
    credentials: "omit",
    redirect: "error",
    signal: AbortSignal.timeout(8000),
  }).catch(() => {
    throw new Error("Could not reach the tracker. Make sure it’s running, then choose Fill again.");
  });
}

async function refreshConnection() {
  const stored = await chrome.storage.session.get(tokenKey);
  connectionSummary.textContent = stored[tokenKey] ? "Autofill profile connected" : "Autofill profile";
}

async function loadProfile() {
  const stored = await chrome.storage.session.get(tokenKey);
  let response = stored[tokenKey] && await requestProfile(stored[tokenKey]);
  if (!response || response.status === 401 || response.status === 403) {
    if (response) await chrome.storage.session.remove(tokenKey);
    status.textContent = "Connecting to the tracker. Sign in if prompted.";
    const result = await chrome.runtime.sendMessage({ type: "autofill:connect" });
    if (result === "profile-required") {
      connection.open = true;
      throw new Error("Save an autofill profile in the tracker, then choose Fill again.");
    }
    if (result !== "connected") {
      throw new Error("Could not connect to the tracker. Make sure it’s running, then choose Fill again.");
    }
    const connected = await chrome.storage.session.get(tokenKey);
    response = await requestProfile(connected[tokenKey]);
  }
  if (!response.ok) throw new Error("Could not load your saved profile. Try Fill again.");
  return response.json();
}

async function recordFill(tabId) {
  await chrome.storage.session.set({ [lastFillKey]: tabId });
  undo.hidden = false;
}

function showReview(plan, tabId, scanId) {
  unfilled.replaceChildren(...[...new Set(plan.review)].map(label => {
    const item = document.createElement("li");
    item.textContent = label;
    return item;
  }));
  answerSuggestions.replaceChildren(...plan.suggestions.map(suggestion => {
    const row = document.createElement("div");
    row.className = "review-answer";
    const label = document.createElement("label");
    label.textContent = suggestion.label;
    const answer = document.createElement(suggestion.editable ? "textarea" : "p");
    if (suggestion.editable) {
      answer.id = `suggestion-${suggestion.index}`;
      label.htmlFor = answer.id;
      answer.rows = 3;
      answer.value = suggestion.value;
      if (suggestion.maxLength > 0) answer.maxLength = suggestion.maxLength;
    } else {
      answer.textContent = suggestion.display;
    }
    const use = document.createElement("button");
    use.type = "button";
    use.className = "text-button";
    use.textContent = "Use answer";
    use.setAttribute("aria-label", `Use answer for ${suggestion.label}`);
    use.addEventListener("click", async () => {
      const value = suggestion.editable ? answer.value.trim() : suggestion.value;
      if (value === "") {
        status.textContent = "Enter an answer before applying it.";
        return;
      }
      use.disabled = true;
      try {
        const tab = await activeTab();
        if (tab.id !== tabId) throw new Error();
        const result = await runInTab(tabId, "write", { scanId, writes: [{ index: suggestion.index, value }] });
        if (!result.filled) throw new Error();
        await recordFill(tabId);
        status.textContent = "Answer added to the form.";
        row.remove();
        reviewPanel.hidden = !answerSuggestions.children.length && !unfilled.children.length;
      } catch {
        status.textContent = "Could not apply the answer. Review the form and choose Fill again.";
      } finally {
        use.disabled = false;
      }
    });
    row.append(label, answer, use);
    return row;
  }));
  reviewPanel.hidden = !plan.review.length && !plan.suggestions.length;
}

function showReferees(referees) {
  refereePanel.hidden = !referees.length;
  refereeSelect.replaceChildren(...referees.map((referee, index) => new Option(referee.name, index)));
  function showFields() {
    refereeStatus.textContent = "";
    const referee = referees[Number(refereeSelect.value)];
    refereeFields.replaceChildren(...["name", "relationship", "phone", "email"]
      .filter(field => referee?.[field]).map(field => {
        const copy = document.createElement("button");
        copy.type = "button";
        copy.className = "text-button";
        copy.textContent = `Copy ${field}`;
        copy.addEventListener("click", async () => {
          try {
            await navigator.clipboard.writeText(referee[field]);
            refereeStatus.textContent = `${referee.name}: ${field} copied.`;
          } catch {
            refereeStatus.textContent = "Clipboard unavailable. Open your profile to copy the details.";
          }
        });
        return copy;
      }));
  }
  refereeSelect.onchange = showFields;
  showFields();
}

chrome.storage.onChanged.addListener((changes, area) => {
  if (area === "session" && changes[tokenKey]) void refreshConnection();
});

fill.addEventListener("click", async () => {
  fill.disabled = true;
  status.textContent = "";
  reviewPanel.hidden = true;
  refereePanel.hidden = true;
  try {
    const tab = await activeTab();
    const scan = await runInTab(tab.id, "scan");
    const profile = await loadProfile();
    const plan = planAutofill(scan.fields, profile);
    showReferees(profile.referees ?? []);
    const frameMessage = scan.inaccessibleFrames ? " Some embedded sections may need manual entry." : "";
    if (!plan.writes.length) {
      showReview(plan, tab.id, scan.scanId);
      status.textContent = (plan.reason ?? (plan.review.length || plan.suggestions.length ? "Review the remaining questions below." : "No blank supported fields to fill.")) + frameMessage;
      return;
    }
    const result = await runInTab(tab.id, "write", { scanId: scan.scanId, writes: plan.writes });
    if (result.stale) {
      status.textContent = "The form changed. Review the page and try Fill again.";
      return;
    }
    showReview(plan, tab.id, scan.scanId);
    const message = [`Filled ${result.filled} ${result.filled === 1 ? "field" : "fields"}.`];
    if (result.failed) message.push(`${result.failed} did not retain a value.`);
    status.textContent = message.join(" ") + frameMessage;
    if (result.filled) await recordFill(tab.id);
  } catch (error) {
    status.textContent = error instanceof Error && /^(Open|Save an autofill profile|Could not (load|connect|reach))/.test(error.message)
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
enableDocumentPicker(activeTab, runInTab);
chrome.storage.session.get(lastFillKey).then(stored => {
  undo.hidden = !stored[lastFillKey];
});
