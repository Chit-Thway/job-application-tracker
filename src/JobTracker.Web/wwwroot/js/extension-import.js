(() => {
  "use strict";

  const MAXIMUM_CAPTURE_PAYLOAD_LENGTH = 120000;
  const SUPPORTED_CAPTURE_SCHEMA_VERSION = 1;
  const captureForm = document.getElementById("extension-capture-form");
  const payloadInput = document.getElementById("PayloadJson");
  const statusMessage = document.getElementById("extension-capture-status");
  if (!captureForm || !payloadInput || !statusMessage) {
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
    if (payload.length > MAXIMUM_CAPTURE_PAYLOAD_LENGTH) {
      throw new Error("Capture is too large.");
    }

    const parsed = JSON.parse(payload);
    if (!parsed
      || typeof parsed !== "object"
      || parsed.version !== SUPPORTED_CAPTURE_SCHEMA_VERSION) {
      throw new Error("Capture format is unsupported.");
    }

    payloadInput.value = payload;
    statusMessage.textContent = "Capture received. Opening your private review draft…";
    captureForm.requestSubmit();
  } catch {
    statusMessage.textContent =
      "That capture could not be read. Return to the job page, reload it, and click the extension again.";
    statusMessage.classList.add("extension-capture-error");
  }
})();
