(() => {
  "use strict";

  const status = document.getElementById("status");
  const form = document.getElementById("handoff-form");
  const payloadInput = document.getElementById("payload-json");

  transferCapture().catch(() => {
    status.textContent =
      "This capture could not be transferred. Close this tab, return to the job page, and try again.";
  });

  async function transferCapture() {
    const captureId = new URL(window.location.href).searchParams.get("id");
    if (!captureId || !/^[0-9a-f-]{36}$/i.test(captureId)) {
      throw new Error("Invalid capture identifier.");
    }

    const storageKey = `capture:${captureId}`;
    const stored = await chrome.storage.session.get(storageKey);
    const handoff = stored[storageKey];
    await chrome.storage.session.remove(storageKey);

    if (!handoff
      || typeof handoff.payload !== "string"
      || typeof handoff.trackerBaseUrl !== "string"
      || handoff.payload.length > 120000
      || Date.now() - Number(handoff.createdAt) > 60000) {
      throw new Error("Capture is unavailable.");
    }

    form.action = `${handoff.trackerBaseUrl}/applications/import/extension/handoff`;
    payloadInput.value = handoff.payload;
    form.submit();
  }
})();
