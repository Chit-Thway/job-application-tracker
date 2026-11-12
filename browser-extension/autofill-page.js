// This function is serialized by Chrome and runs in the active tab's isolated world.
export async function accessApplicationPage(operation, request = {}) {
  const supported = "input,select,textarea,button[role=combobox],button[aria-haspopup=listbox]";
  const inputTypes = ["text", "email", "tel", "url", "number", "month", "date", "checkbox", "radio", "file"];
  const stateKey = "__jobTrackerAutofillV1";
  const state = globalThis[stateKey] ??= {};
  const text = value => String(value ?? "").replace(/\s+/g, " ").trim().slice(0, 180);
  const checkable = field => field.type === "checkbox" || field.type === "radio";
  const valueOf = field => checkable(field) ? field.checked : field.value;

  function radioGroup(field) {
    if (!field.name) return [];
    return [...field.getRootNode().querySelectorAll(`input[type=radio][name="${CSS.escape(field.name)}"]`)]
      .filter(option => option.form === field.form);
  }

  function visible(field) {
    if (!field.isConnected || field.matches(":disabled, [readonly], [aria-disabled=true], [multiple]")
        || field.closest("[hidden], [inert], [aria-hidden=true], details:not([open])")) return false;
    try {
      const window = field.ownerDocument.defaultView;
      return window.document === field.ownerDocument && field.getClientRects().length > 0
        && window.getComputedStyle(field).visibility === "visible"
        && !field.closest("dialog:not([open])");
    } catch {
      return false;
    }
  }

  function availableFile(field) {
    return visible(field) || (field.isConnected && !field.matches(":disabled, [multiple], [aria-disabled=true]") &&
      !field.closest("[inert]") && [...(field.labels ?? [])].some(visible));
  }

  function blank(field) {
    if (field.tagName === "BUTTON") return /^(?:select|choose|please select)(?: (?:one|an option))?\s*[…\.]*$/i.test(text(field.textContent));
    if (field.type === "radio") return radioGroup(field).every(option => !option.checked);
    if (field.type === "checkbox") return !field.checked;
    if (field.tagName === "SELECT") {
      const selected = field.selectedOptions[0];
      return !selected?.value || (selected.disabled && /^(?:select|choose|please select)\b/i.test(selected.text));
    }
    return !field.value.trim();
  }

  function fieldLabel(field) {
    const root = field.getRootNode();
    const names = (field.getAttribute("aria-labelledby") ?? "").split(/\s+/)
      .map(id => root.getElementById(id)?.textContent).filter(Boolean);
    if (names.length) return text(names.join(" "));
    const ariaLabel = field.getAttribute("aria-label");
    if (ariaLabel) return text(ariaLabel);
    return text([...(field.labels ?? [])].map(label => {
      const copy = label.cloneNode(true);
      for (const control of copy.querySelectorAll("input, select, textarea, button, [aria-hidden=true]")) {
        control.remove();
      }
      return copy.textContent;
    }).join(" "));
  }

  function sectionContext(field, form) {
    const headings = [];
    let group = null;
    for (let node = field.parentElement, depth = 0; node && depth < 12; node = node.parentElement, depth++) {
      const heading = text(node.getAttribute("aria-label") ||
        node.querySelector(":scope > legend, :scope > h2, :scope > h3, :scope > h4, :scope > [role=heading]")?.textContent);
      if (heading) headings.push(heading);
      if (!group) {
        const label = heading.replace(/[–—:-]/g, " ").trim();
        const automationId = node.getAttribute("data-automation-id") ?? "";
        const headingMatch = /^(work experiences?|experience|employment history|employment|previous employment|work history|jobs?|education|education history|qualifications?|certifications?|licen[cs]es?|professional certifications?)(?:\s*#?\s*(\d+))?$/i.exec(label);
        const idMatch = /^(workExperience|education|certification)-(\d+)$/i.exec(automationId);
        const match = headingMatch || idMatch;
        if (match) {
          const name = match[1].toLowerCase();
          const kind = /education|qualification/.test(name) ? "education"
            : /certification|licen[cs]e/.test(name) ? "certifications" : "experience";
          const zeroBased = idMatch && (match[2] === "0" ||
            form.querySelector(`[data-automation-id="${match[1]}-0"]`));
          const ordinal = match[2] ? Number(match[2]) - (zeroBased ? 0 : 1) : null;
          group = { node, kind, ordinal: ordinal !== null && ordinal >= 0 ? ordinal : null };
        }
      }
      if (node === form) break;
    }
    return { group, context: headings.join(" / ") };
  }

  function discovery(forAttachment) {
    const fields = [];
    const references = [];
    const ids = new Map();
    const idFor = node => {
      if (!ids.has(node)) ids.set(node, ids.size);
      return ids.get(node);
    };
    let inaccessibleFrames = 0;
    const roots = [document];
    const visited = new Set();
    for (const root of roots) {
      if (visited.has(root) || visited.size >= 30 || fields.length >= 350) continue;
      visited.add(root);
      const owner = root.ownerDocument ?? root;
      const heading = text(owner.querySelector("main h1, h1")?.textContent);
      const location = owner.defaultView?.location.pathname ?? "";
      const applicationPage = /\b(apply|application|candidate|my experience)\b/i.test(`${heading} ${location}`);
      const active = root.activeElement;
      for (const field of root.querySelectorAll(supported)) {
        if (fields.length >= 350) break;
        if (field.tagName === "INPUT" && !inputTypes.includes(field.type)) continue;
        if (field.type === "file" ? !availableFile(field) : !visible(field)) continue;
        const form = field.form ?? field.closest("[role=form]") ?? root;
        const { group, context } = sectionContext(field, form);
        const applicationSection = /\b(applicant|candidate|application|personal information|contact details)\b/i.test(context);
        const label = fieldLabel(field);
        const radioContainer = field.type === "radio" ? field.closest("fieldset, [role=radiogroup]") : null;
        const radioQuestion = text(radioContainer?.getAttribute("aria-label") ||
          radioContainer?.querySelector(":scope > legend")?.textContent ||
          field.getRootNode().getElementById(radioContainer?.getAttribute("aria-labelledby") ?? "")?.textContent);
        const isBlank = blank(field);
        const anchor = group && /^(employer|company( name)?|employer name|job title|position|title|school|institution|university|qualification|degree|certification( name)?|issuer)\s*\*?$/i.test(label);
        fields.push({
          index: references.length,
          tag: field.tagName,
          type: field.type,
          label: radioContainer ? radioQuestion : label,
          radioChoice: radioQuestion && field.name && /^(yes|no)$/i.test(label) &&
            radioGroup(field).filter(option => fieldLabel(option).toLowerCase() === label.toLowerCase()).length === 1 ? label : "",
          context,
          name: text(field.name),
          id: text(field.id),
          autocomplete: text(field.getAttribute("autocomplete")),
          placeholder: text(field.getAttribute("placeholder")),
          maxLength: field.maxLength,
          customPicker: field.tagName === "BUTTON" || field.tagName !== "SELECT" &&
            (field.getAttribute("role") === "combobox" || field.hasAttribute("aria-autocomplete")),
          blank: isBlank,
          anchorValue: anchor && !isBlank ? text(field.value) : "",
          options: field.tagName === "SELECT" ? [...field.options]
            .filter(option => option.value && !option.matches(":disabled"))
            .slice(0, 300).map(option => ({ value: option.value, label: text(option.text) })) : [],
          formKey: idFor(form),
          hasPassword: form.querySelector("input[type=password]") !== null,
          focused: active === field,
          applicationContext: applicationPage || applicationSection,
          applicationSection,
          group: group?.kind ?? null,
          groupKey: group ? idFor(group.node) : null,
          groupIndex: group?.ordinal ?? null,
        });
        references.push({ field, label, form, associatedForm: field.form,
          groupNode: group?.node, context, type: field.type, before: valueOf(field) });
      }
      const walker = owner.createTreeWalker(root, NodeFilter.SHOW_ELEMENT);
      for (let node = walker.nextNode(), count = 0; node && count < 5000; node = walker.nextNode(), count++) {
        if (roots.length >= 30) break;
        if (node.shadowRoot) roots.push(node.shadowRoot);
        if (node.tagName === "IFRAME" && visible(node)) {
          if (node.contentDocument) roots.push(node.contentDocument);
          else inaccessibleFrames++;
        }
      }
    }
    const scanId = [...crypto.getRandomValues(new Uint32Array(4))].join("-");
    state[forAttachment ? "attachmentScan" : "scan"] = { scanId, references };
    return { scanId, fields, inaccessibleFrames };
  }

  function setValue(field, value) {
    const window = field.ownerDocument.defaultView;
    const prototype = field.tagName === "SELECT" ? window.HTMLSelectElement.prototype :
      field.tagName === "TEXTAREA" ? window.HTMLTextAreaElement.prototype : window.HTMLInputElement.prototype;
    const property = checkable(field) ? "checked" : "value";
    Object.getOwnPropertyDescriptor(prototype, property).set.call(field, value);
    field.dispatchEvent(new window.Event("input", { bubbles: true, composed: true }));
    field.dispatchEvent(new window.Event("change", { bubbles: true }));
  }

  if (operation === "scan") return discovery(request.forAttachment);
  if (operation === "undo") {
    const last = state.lastWrites ?? [];
    state.lastWrites = [];
    let undone = 0;
    for (const { field, before, after, label, context, form, groupNode } of last) {
      const current = sectionContext(field, form);
      if (visible(field) && fieldLabel(field) === label && valueOf(field) === after
          && current.context === context && current.group?.node === groupNode) {
        setValue(field, before);
        if (valueOf(field) === before) undone++;
      }
    }
    return { undone };
  }

  const scan = operation === "attach" ? state.attachmentScan : state.scan;
  if (!scan || scan.scanId !== request.scanId) return { stale: true };

  if (operation === "attach") {
    state.attachmentScan = null;
    const record = scan.references[request.index];
    if (!record || record.type !== "file" || !availableFile(record.field) || !blank(record.field)
        || record.field.type !== "file" || record.field.form !== record.associatedForm
        || fieldLabel(record.field) !== record.label || sectionContext(record.field, record.form).context !== record.context) {
      return { stale: true };
    }
    const { field } = record;
    const { name, mime, data } = request.document;
    const extension = name.slice(name.lastIndexOf(".")).toLowerCase();
    const accepted = field.accept.toLowerCase().split(",").map(value => value.trim()).filter(Boolean);
    if (accepted.length && !accepted.some(value => value === extension || value === mime)) return { unsupported: true };
    const window = field.ownerDocument.defaultView;
    const transfer = new window.DataTransfer();
    transfer.items.add(new window.File([Uint8Array.from(atob(data), character => character.charCodeAt(0))], name, { type: mime }));
    field.files = transfer.files;
    field.dispatchEvent(new window.Event("input", { bubbles: true, composed: true }));
    field.dispatchEvent(new window.Event("change", { bubbles: true }));
    await new Promise(resolve => setTimeout(resolve, 80));
    return { attached: field.files[0]?.name === name };
  }

  if (operation !== "write") return { stale: true };

  const changed = [];
  for (const { index, value } of request.writes) {
    const record = scan.references[index];
    if (!record) continue;
    const { field, label, form, associatedForm, type, before, groupNode, context } = record;
    if (type === "file" || field.tagName === "BUTTON" || !visible(field) || !blank(field) || field.form !== associatedForm || field.type !== type
        || fieldLabel(field) !== label || valueOf(field) !== before) continue;
    const current = sectionContext(field, form);
    if (current.context !== context || current.group?.node !== groupNode) continue;
    if (field.tagName === "SELECT" && ![...field.options].some(option =>
      !option.matches(":disabled") && option.value === value)) continue;
    setValue(field, value);
    changed.push({ ...record, after: value });
  }
  await new Promise(resolve => setTimeout(resolve, 80));
  const retained = changed.filter(({ field, after, before }) => {
    if (!visible(field) || valueOf(field) !== after) return false;
    if (field.validity.valid) return true;
    setValue(field, before);
    return false;
  });
  if (retained.length) state.lastWrites = retained;
  return { filled: retained.length, failed: request.writes.length - retained.length };
}
