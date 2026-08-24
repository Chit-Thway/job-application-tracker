(() => {
  "use strict";

  const mobileNavigation = window.matchMedia("(max-width: 1088px)");

  const navigationElements = () => [...document.querySelectorAll("[data-site-nav]")];

  const resetNavigation = () => {
    for (const navigation of navigationElements()) {
      const toggle = navigation.querySelector(".site-nav-toggle");
      if (toggle instanceof HTMLInputElement) {
        toggle.checked = false;
      }
    }
  };

  const closeMobileNavigation = (navigation, restoreFocus = false) => {
    if (!mobileNavigation.matches) {
      return;
    }

    const toggle = navigation.querySelector(".site-nav-toggle");
    if (!(toggle instanceof HTMLInputElement) || !toggle.checked) {
      return;
    }

    toggle.checked = false;
    if (restoreFocus) {
      navigation.querySelector(".site-nav-summary")?.focus();
    }
  };



  document.addEventListener("click", event => {
    if (!(event.target instanceof Element)) {
      return;
    }

    for (const navigation of navigationElements()) {
      if (event.target.closest(".nav-panel a")) {
        closeMobileNavigation(navigation);
      } else if (!navigation.contains(event.target)) {
        closeMobileNavigation(navigation);
      }
    }
  });

  document.addEventListener("keydown", event => {
    if (event.key !== "Escape") {
      return;
    }

    for (const navigation of navigationElements()) {
      closeMobileNavigation(navigation, true);
    }
  });

  if (typeof mobileNavigation.addEventListener === "function") {
    mobileNavigation.addEventListener("change", resetNavigation);
  } else {
    mobileNavigation.addListener(resetNavigation);
  }
})();
