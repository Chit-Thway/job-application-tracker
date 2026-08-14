(() => {
    const results = document.querySelector("[data-application-results]");
    if (!results) {
        return;
    }

    const cards = [...results.querySelectorAll(".application-record")];
    const viewButtons = [...document.querySelectorAll("[data-application-view]")];
    const selectionToggle = document.querySelector("[data-selection-toggle]");
    const selectionClose = document.querySelector("[data-selection-close]");
    const bulkForm = document.getElementById("bulk-application-form");
    const selectedCount = document.querySelector("[data-selected-count]");
    const checkboxes = cards
        .map(card => card.querySelector('input[name="SelectedApplicationIds"]'))
        .filter(Boolean);

    const setView = view => {
        const listView = view === "list";
        results.classList.toggle("is-list-view", listView);
        viewButtons.forEach(button => {
            const active = button.dataset.applicationView === view;
            button.classList.toggle("is-active", active);
            button.setAttribute("aria-pressed", active.toString());
        });
        try {
            localStorage.setItem("job-tracker-application-view", view);
        } catch {
            // The view still works when browser storage is unavailable.
        }
    };

    viewButtons.forEach(button => {
        button.addEventListener("click", () => setView(button.dataset.applicationView));
    });

    let initialView = "cards";
    try {
        initialView = localStorage.getItem("job-tracker-application-view") === "list"
            ? "list"
            : "cards";
    } catch {
        initialView = "cards";
    }
    setView(initialView);

    const updateSelection = () => {
        const count = checkboxes.filter(checkbox => checkbox.checked).length;
        if (selectedCount) {
            selectedCount.textContent = count.toString();
        }
        bulkForm?.querySelectorAll('button[type="submit"]').forEach(button => {
            button.disabled = count === 0;
        });
        cards.forEach(card => {
            const checkbox = card.querySelector('input[name="SelectedApplicationIds"]');
            card.classList.toggle("is-selected", checkbox?.checked === true);
        });
    };

    const setSelectionMode = active => {
        results.classList.toggle("is-selection-mode", active);
        if (bulkForm) {
            bulkForm.hidden = !active;
        }
        selectionToggle?.setAttribute("aria-expanded", active.toString());
        if (selectionToggle) {
            selectionToggle.textContent = active ? "Selecting" : "Select";
        }
        if (!active) {
            checkboxes.forEach(checkbox => {
                checkbox.checked = false;
            });
        }
        updateSelection();
    };

    checkboxes.forEach(checkbox => checkbox.addEventListener("change", updateSelection));
    cards.forEach(card => {
        card.addEventListener("click", event => {
            if (!results.classList.contains("is-selection-mode")) {
                return;
            }

            const target = event.target instanceof Element ? event.target : null;
            if (target?.closest(".application-select")) {
                return;
            }

            const checkbox = card.querySelector('input[name="SelectedApplicationIds"]');
            if (!checkbox) {
                return;
            }

            event.preventDefault();
            checkbox.checked = !checkbox.checked;
            checkbox.dispatchEvent(new Event("change", { bubbles: true }));
        });
    });
    selectionToggle?.addEventListener("click", () => {
        setSelectionMode(!results.classList.contains("is-selection-mode"));
    });
    selectionClose?.addEventListener("click", () => setSelectionMode(false));

    document.querySelectorAll("[data-select-preset]").forEach(button => {
        button.addEventListener("click", () => {
            const preset = button.dataset.selectPreset;
            cards.forEach(card => {
                const checkbox = card.querySelector('input[name="SelectedApplicationIds"]');
                if (!checkbox) {
                    return;
                }

                checkbox.checked = preset === "all"
                    || (preset === "saved" && card.dataset.saved === "true")
                    || (preset === "unsaved" && card.dataset.saved === "false")
                    || (preset === "scheduled" && card.dataset.deletionScheduled === "true");
            });
            updateSelection();
        });
    });

    setSelectionMode(false);
})();
