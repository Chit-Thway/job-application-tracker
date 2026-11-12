(() => {
  const sectionLinks = document.querySelectorAll(".autofill-nav a[href^='#']");
  const result = document.querySelector(
    "[data-resume-error]:not([hidden]), [data-resume-result], [data-profile-validation].validation-summary-errors");
  const importPanel = document.querySelector("[data-resume-picker]");

  function markSection() {
    const location = window.location.hash || (result?.matches("[data-resume-result]") ? "#resume-review" : "#resume-import");
    for (const link of sectionLinks) {
      if (link.hash === location) link.setAttribute("aria-current", "location");
      else link.removeAttribute("aria-current");
    }
  }

  markSection();
  window.addEventListener("hashchange", markSection);
  document.querySelector(".autofill-nav a[href='#resume-import']")?.addEventListener("click", () => {
    if (importPanel) importPanel.open = true;
  });
  result?.focus();

  const upload = document.querySelector("[data-resume-upload]");
  const picker = upload?.querySelector("input[type='file']");
  const fileError = upload?.querySelector("[data-resume-error]");
  const fileStatus = upload?.querySelector("[data-resume-selection]");

  picker?.addEventListener("change", () => {
    const file = picker.files[0];
    let message = "";
    if (file && !/\.(pdf|docx)$/i.test(file.name)) message = "Choose a PDF or Word (.docx) résumé.";
    else if (file?.size === 0) message = "The selected file is empty.";
    else if (file?.size > 5 * 1024 * 1024) message = "Choose a résumé that is 5 MB or smaller.";

    picker.setCustomValidity(message);
    if (message) picker.setAttribute("aria-invalid", "true");
    else picker.removeAttribute("aria-invalid");
    fileError.hidden = !message;
    fileError.textContent = message;
    fileStatus.dataset.feedback = file && !message ? "success" : "";
    fileStatus.textContent = file && !message ? "File selected. Ready to extract." : "";
  });

  for (const button of document.querySelectorAll("button[data-pending-label]")) {
    const label = button.textContent;
    button.form.addEventListener("submit", event => {
      if (button.disabled) {
        event.preventDefault();
        return;
      }
      button.disabled = true;
      button.setAttribute("aria-busy", "true");
      button.textContent = button.dataset.pendingLabel;
      if (button.form === upload) {
        fileStatus.dataset.feedback = "";
        fileStatus.textContent = "Reading your résumé.";
      }
    });
    window.addEventListener("pageshow", event => {
      if (!event.persisted) return;
      button.disabled = false;
      button.removeAttribute("aria-busy");
      button.textContent = label;
      if (button.form === upload) picker.dispatchEvent(new Event("change"));
    });
  }

  function markFromResume(field) {
    const label = field?.labels?.[0];
    if (!label || (field.type === "checkbox" ? !field.checked : !field.value.trim())) return;

    const badge = document.createElement("span");
    badge.className = "resume-field-badge";
    badge.textContent = "From résumé";
    label.append(" ", badge);
    field.addEventListener("input", () => badge.remove(), { once: true });
  }

  const profile = document.querySelector("form[data-resume-fields]");
  for (const name of (profile?.dataset.resumeFields ?? "").split(",").filter(Boolean)) {
    markFromResume(profile.elements.namedItem(name));
  }

  for (const list of document.querySelectorAll("[data-entry-list]")) {
    const entries = list.querySelector("[data-entries]");
    const add = list.querySelector("[data-add-entry]");
    const template = list.querySelector("template");
    const empty = list.querySelector("[data-entry-empty]");
    const feedback = list.querySelector("[data-entry-status]");

    function reindex() {
      for (const [index, entry] of [...entries.children].entries()) {
        entry.querySelector("[data-entry-number]").textContent = index + 1;
        const labels = [...entry.querySelectorAll("label[for]")];
        for (const field of entry.querySelectorAll("[name]")) {
          field.name = field.name.replace(/\[(?:\d+|__index__)\]/, `[${index}]`);
          if (field.id) {
            const label = labels.find(label => label.htmlFor === field.id);
            field.id = field.name.replace(/[.\[\]]/g, "_");
            if (label) label.htmlFor = field.id;
          }
        }
      }
      add.disabled = entries.children.length >= 20;
      empty.hidden = entries.children.length > 0;
    }

    add.addEventListener("click", () => {
      const entry = template.content.firstElementChild.cloneNode(true);
      entry.dataset.newEntry = "";
      entries.append(entry);
      reindex();
      entry.querySelector("input, select, textarea").focus();
      feedback.textContent = "Entry added to the draft.";
    });

    entries.addEventListener("click", async event => {
      const remove = event.target.closest("[data-remove-entry]");
      if (remove) {
        remove.closest("[data-entry]").remove();
        reindex();
        add.focus();
        feedback.textContent = "Entry removed from the draft.";
      }

      const copy = event.target.closest("[data-copy-role]");
      if (copy) {
        const role = copy.closest("[data-entry]");
        const value = name => role.querySelector(`[name$=".${name}"]`).value.trim();
        const end = role.querySelector("[data-current-role]").checked ? "Present" : value("EndMonth");
        const dates = [value("StartMonth"), end].filter(Boolean).join(" to ");
        const text = [[value("Title"), value("Employer")].filter(Boolean).join(" at "),
          value("Location"), dates, value("Responsibilities")].filter(Boolean).join("\n");
        const status = role.querySelector("[data-copy-status]");
        try {
          if (text) await navigator.clipboard.writeText(text);
          status.dataset.feedback = text ? "success" : "";
          status.textContent = text ? "Role copied." : "Enter some role details first.";
        } catch {
          status.dataset.feedback = "error";
          status.textContent = "Clipboard unavailable. Copy the fields directly.";
        }
      }
    });

    entries.addEventListener("change", event => {
      if (event.target.matches("[data-current-role]")) {
        event.target.closest("[data-entry]").querySelector("[data-end-month]").hidden = event.target.checked;
      }
    });
    reindex();

    const importedCount = Number(list.dataset.resumeAdded);
    if (importedCount > 0) {
      for (const entry of [...entries.children].slice(-importedCount)) {
        for (const field of entry.querySelectorAll("input, textarea, select")) {
          markFromResume(field);
        }
      }
    }
  }
})();
