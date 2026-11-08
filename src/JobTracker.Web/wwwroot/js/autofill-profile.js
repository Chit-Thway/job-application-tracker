(() => {
  for (const list of document.querySelectorAll("[data-entry-list]")) {
    const entries = list.querySelector("[data-entries]");
    const add = list.querySelector("[data-add-entry]");
    const template = list.querySelector("template");

    function reindex() {
      for (const [index, entry] of [...entries.children].entries()) {
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
    }

    add.addEventListener("click", () => {
      entries.append(template.content.cloneNode(true));
      reindex();
      entries.lastElementChild.querySelector("input").focus();
    });

    entries.addEventListener("click", async event => {
      const remove = event.target.closest("[data-remove-entry]");
      if (remove) {
        remove.closest("[data-entry]").remove();
        reindex();
        add.focus();
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
          status.textContent = text ? "Role copied." : "Enter some role details first.";
        } catch {
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
  }
})();
