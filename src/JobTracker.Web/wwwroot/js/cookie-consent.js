(() => {
  "use strict";

  const storageKey = "job-tracker-cookie-choice-v1";
  const notice = document.querySelector("[data-cookie-notice]");
  const acknowledge = notice?.querySelector("[data-cookie-acknowledge]");

  const showNotice = () => {
    if (notice) notice.hidden = false;
  };

  if (!window.localStorage.getItem(storageKey)) showNotice();

  acknowledge?.addEventListener("click", () => {
    window.localStorage.setItem(storageKey, new Date().toISOString());
    notice.hidden = true;
  });

  for (const reset of document.querySelectorAll("[data-cookie-reset]")) {
    reset.addEventListener("click", () => {
      window.localStorage.removeItem(storageKey);
      showNotice();
      notice?.focus();
    });
  }
})();
