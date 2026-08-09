(() => {
  "use strict";

  const form = document.getElementById("extension-capture-form");
  const input = document.getElementById("PayloadJson");
  const status = document.getElementById("extension-capture-status");
  if (!form || !input || !status) {
    return;
  }

  const parameters = new URLSearchParams(window.location.hash.slice(1));
  const encodedCapture = parameters.get("capture");
  if (!encodedCapture) {
    return;
  }

  window.history.replaceState(null, "", `${window.location.pathname}${window.location.search}`);

  try {
    const base64 = encodedCapture.replace(/-/g, "+").replace(/_/g, "/");
    const padded = base64.padEnd(Math.ceil(base64.length / 4) * 4, "=");
    const binary = window.atob(padded);
    const bytes = Uint8Array.from(binary, character => character.charCodeAt(0));
    const payload = new TextDecoder("utf-8", { fatal: true }).decode(bytes);
    if (payload.length > 120000) {
      throw new Error("Capture is too large.");
    }

    const parsed = JSON.parse(payload);
    if (!parsed || typeof parsed !== "object" || parsed.version !== 1) {
      throw new Error("Capture format is unsupported.");
    }

    input.value = payload;
    status.textContent = "Capture received. Opening your private review draft…";
    form.requestSubmit();
  } catch {
    status.textContent =
      "That capture could not be read. Return to the job page, reload it, and click the extension again.";
    status.classList.add("extension-capture-error");
  }
})();
