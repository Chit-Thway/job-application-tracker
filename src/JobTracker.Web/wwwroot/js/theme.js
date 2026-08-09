(function () {
  "use strict";

  const storageKey = "job-tracker-theme";
  const root = document.documentElement;

  function readPreference() {
    try {
      const savedTheme = window.localStorage.getItem(storageKey);
      return savedTheme === "dark" || savedTheme === "light" ? savedTheme : "light";
    } catch {
      return "light";
    }
  }

  function applyTheme(theme, persist) {
    root.dataset.theme = theme;

    if (persist) {
      try {
        window.localStorage.setItem(storageKey, theme);
      } catch {
        // The theme still works for this page when storage is unavailable.
      }
    }

    const toggle = document.getElementById("theme-toggle");
    if (!toggle) {
      return;
    }

    const isDark = theme === "dark";
    const nextTheme = isDark ? "light" : "dark";
    const label = `Switch to ${nextTheme} mode`;
    toggle.setAttribute("aria-pressed", String(isDark));
    toggle.setAttribute("aria-label", label);
    toggle.setAttribute("title", label);

    const status = toggle.querySelector("[data-theme-label]");
    if (status) {
      status.textContent = `${isDark ? "Dark" : "Light"} theme active`;
    }
  }

  applyTheme(readPreference(), false);

  document.addEventListener("DOMContentLoaded", function () {
    const toggle = document.getElementById("theme-toggle");
    applyTheme(root.dataset.theme || "light", false);
    root.classList.add("theme-ready");

    toggle?.addEventListener("click", function () {
      applyTheme(root.dataset.theme === "dark" ? "light" : "dark", true);
    });
  });

  window.addEventListener("storage", function (event) {
    if (event.key === storageKey && (event.newValue === "dark" || event.newValue === "light")) {
      applyTheme(event.newValue, false);
    }
  });
})();
