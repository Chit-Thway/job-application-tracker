(() => {
  "use strict";

  const form = document.querySelector("[data-verification-resend]");
  if (!form) return;

  const button = form.querySelector("[data-resend-button]");
  const label = form.querySelector("[data-resend-label]");
  let seconds = Number.parseInt(form.dataset.resendSeconds ?? "0", 10);
  if (!button || !label || !Number.isFinite(seconds)) return;

  const render = () => {
    const waiting = seconds > 0;
    button.disabled = waiting;
    label.textContent = waiting ? `Resend code in ${seconds}s` : "Resend code";
    if (!waiting) return;

    window.setTimeout(() => {
      seconds -= 1;
      render();
    }, 1000);
  };

  render();
})();
