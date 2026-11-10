import { planAutofill } from "./autofill-matching.js";

const maximumBytes = 2 * 1024 * 1024;
const fileTypes = new Map([
  ["pdf", "application/pdf"],
  ["docx", "application/vnd.openxmlformats-officedocument.wordprocessingml.document"],
]);

export function enableDocumentPicker(activeTab, runInTab) {
  const form = document.getElementById("document-picker");
  const kind = document.getElementById("document-kind");
  const picker = document.getElementById("document-file");
  const selected = document.getElementById("selected-document");
  const attach = document.getElementById("attach-document");
  const remove = document.getElementById("forget-document");
  const status = document.getElementById("document-status");
  const key = () => `autofill:document:${kind.value}`;

  async function savedFile(slot) {
    const files = await chrome.storage.local.get(slot);
    return files[slot];
  }

  async function refresh() {
    const slot = key();
    const file = await savedFile(slot);
    if (slot !== key()) return;
    selected.textContent = file ? `Selected file: ${file.name}` : "No file saved for this document type.";
    attach.disabled = !file;
    remove.hidden = !file;
  }

  function updateSelection() {
    void refresh().catch(() => { status.textContent = "Could not read saved documents."; });
  }

  kind.addEventListener("change", () => {
    status.textContent = "";
    updateSelection();
  });

  picker.addEventListener("change", async () => {
    const file = picker.files[0];
    picker.value = "";
    if (!file) return;
    const mime = fileTypes.get(file.name.split(".").pop().toLowerCase());
    if (!mime || !file.size || file.size > maximumBytes) {
      status.textContent = "Choose a PDF or Word (.docx) file up to 2 MB.";
      return;
    }
    const slot = key();
    attach.disabled = true;
    try {
      const bytes = new Uint8Array(await file.arrayBuffer());
      let binary = "";
      for (let offset = 0; offset < bytes.length; offset += 16384) {
        binary += String.fromCharCode(...bytes.subarray(offset, offset + 16384));
      }
      await chrome.storage.local.set({ [slot]: { name: file.name, mime, data: btoa(binary) } });
      status.textContent = "File saved in this browser.";
    } catch {
      status.textContent = "Could not save the file in this browser.";
    } finally {
      updateSelection();
    }
  });

  remove.addEventListener("click", async () => {
    try {
      await chrome.storage.local.remove(key());
      status.textContent = "Saved file removed.";
      updateSelection();
    } catch {
      status.textContent = "Could not remove the saved file.";
    }
  });

  form.addEventListener("submit", async event => {
    event.preventDefault();
    const type = kind.value;
    const slot = key();
    attach.disabled = true;
    kind.disabled = picker.disabled = remove.disabled = true;
    try {
      const tab = await activeTab();
      const file = await savedFile(slot);
      if (!file || slot !== key()) return;
      const scan = await runInTab(tab.id, "scan", { forAttachment: true });
      const matches = planAutofill(scan.fields, {}).attachments.filter(field => field.kind === type);
      if (matches.length !== 1) {
        status.textContent = "Could not find one empty upload field for this document. Attach it directly on the form.";
        return;
      }
      const result = await runInTab(tab.id, "attach", { scanId: scan.scanId, index: matches[0].index, document: file });
      status.textContent = result.attached ? `Added ${file.name}. Check that the employer’s form accepted it.` :
        "Could not attach this file. Use the employer’s upload control.";
    } catch (error) {
      status.textContent = error instanceof Error && error.message.startsWith("Open") ? error.message :
        "Could not attach this file. Use the employer’s upload control.";
    } finally {
      kind.disabled = picker.disabled = remove.disabled = false;
      updateSelection();
    }
  });

  updateSelection();
}
