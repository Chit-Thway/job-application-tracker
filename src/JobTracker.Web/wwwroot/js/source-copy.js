(() => {
  "use strict";

  const fallbackCopy = text => {
    const input = document.createElement("textarea");
    input.value = text;
    input.setAttribute("readonly", "");
    input.style.position = "fixed";
    input.style.opacity = "0";
    document.body.append(input);
    input.select();

    try {
      return document.execCommand("copy");
    } finally {
      input.remove();
    }
  };

  const copyText = async text => {
    if (navigator.clipboard) {
      try {
        await navigator.clipboard.writeText(text);
        return true;
      } catch {
        return fallbackCopy(text);
      }
    }

    return fallbackCopy(text);
  };

  for (const button of document.querySelectorAll("[data-copy-source]")) {
    button.dataset.copyReady = "true";
    button.addEventListener("click", async () => {
      const source = document.getElementById(button.dataset.copyTarget ?? "");
      const status = document.getElementById(button.dataset.copyStatus ?? "");
      if (!source) {
        return;
      }

      try {
        if (!await copyText(source.textContent ?? "")) {
          throw new Error("Copy command was unavailable.");
        }

        button.classList.add("is-copied");
        button.setAttribute("aria-label", "Original source copied");
        button.setAttribute("title", "Copied");
        if (status) {
          status.textContent = "Original source copied to the clipboard.";
        }

        window.setTimeout(() => {
          button.classList.remove("is-copied");
          button.setAttribute("aria-label", "Copy the complete original source");
          button.setAttribute("title", "Copy original source");
        }, 1800);
      } catch {
        if (status) {
          status.textContent = "The original source could not be copied. Select the text and copy it manually.";
        }
      }
    });
  }
})();
