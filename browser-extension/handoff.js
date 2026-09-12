(() => {
  "use strict";

  const CAPTURE_ID_PATTERN = /^[0-9a-f-]{36}$/i;
  const CAPTURE_STORAGE_KEY_PREFIX = "capture:";
  const MAXIMUM_CAPTURE_PAYLOAD_LENGTH = 120000;
  const MAXIMUM_CAPTURE_AGE_MILLISECONDS = 60000;
  const statusMessage = document.getElementById("status");
  const handoffForm = document.getElementById("handoff-form");
  const payloadInput = document.getElementById("payload-json");

  transferCapture().catch(() => {
    statusMessage.textContent =
      "This capture could not be transferred. Close this tab, return to the job page, and try again.";
  });

  async function transferCapture() {
    const captureId = new URL(window.location.href).searchParams.get("id");
    if (!captureId || !CAPTURE_ID_PATTERN.test(captureId)) {
      throw new Error("Invalid capture identifier.");
    }

    const storageKey = `${CAPTURE_STORAGE_KEY_PREFIX}${captureId}`;
    const stored = await chrome.storage.session.get(storageKey);
    const storedHandoff = stored[storageKey];
    await chrome.storage.session.remove(storageKey);

    if (!isValidStoredHandoff(storedHandoff)) {
      throw new Error("Capture is unavailable.");
    }

    handoffForm.action = `${storedHandoff.trackerBaseUrl}/applications/import/extension/handoff`;
    payloadInput.value = storedHandoff.payload;
    handoffForm.submit();
  }

  function isValidStoredHandoff(storedHandoff) {
    if (!storedHandoff
      || typeof storedHandoff.payload !== "string"
      || typeof storedHandoff.trackerBaseUrl !== "string") {
      return false;
    }

    const captureAge = Date.now() - Number(storedHandoff.createdAt);
    return storedHandoff.payload.length <= MAXIMUM_CAPTURE_PAYLOAD_LENGTH
      && captureAge <= MAXIMUM_CAPTURE_AGE_MILLISECONDS;
  }
})();
