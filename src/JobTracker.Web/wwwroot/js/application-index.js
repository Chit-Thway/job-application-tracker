(() => {
    const initializeApplicationResults = () => {
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
            localStorage.setItem("job-tracker-application-view-v2", view);
        } catch {
            // The view still works when browser storage is unavailable.
        }
    };

    viewButtons.forEach(button => {
        button.addEventListener("click", () => setView(button.dataset.applicationView));
    });

    let initialView = "list";
    try {
        initialView = localStorage.getItem("job-tracker-application-view-v2") === "cards"
            ? "cards"
            : "list";
    } catch {
        initialView = "list";
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

    const statusClasses = [
        "status-stage-applied",
        "status-stage-screening",
        "status-stage-assessment",
        "status-stage-interview",
        "status-stage-offer",
        "status-outcome-active",
        "status-outcome-accepted",
        "status-outcome-rejected",
        "status-outcome-withdrawn",
        "status-outcome-ghosted"
    ];
    const stageClasses = {
        Applied: "status-stage-applied",
        Screening: "status-stage-screening",
        Assessment: "status-stage-assessment",
        Interview: "status-stage-interview",
        Offer: "status-stage-offer"
    };
    const outcomeClasses = {
        Active: "status-outcome-active",
        Accepted: "status-outcome-accepted",
        Rejected: "status-outcome-rejected",
        Withdrawn: "status-outcome-withdrawn",
        Ghosted: "status-outcome-ghosted"
    };

    document.querySelectorAll("[data-inline-status-form]").forEach(form => {
        const selects = [...form.querySelectorAll("[data-status-select]")];
        const editor = form.querySelector("[data-status-editor]");
        const note = editor?.querySelector("textarea");
        const discard = form.querySelector("[data-status-discard]");

        const updateTone = select => {
            const field = select.closest("[data-status-tone]");
            if (!field) {
                return;
            }

            field.classList.remove(...statusClasses);
            const tones = select.dataset.statusKind === "stage" ? stageClasses : outcomeClasses;
            const tone = tones[select.value];
            if (tone) {
                field.classList.add(tone);
            }
        };

        const updateEditor = () => {
            const changed = selects.some(select => select.value !== select.dataset.originalValue);
            if (editor) {
                editor.hidden = !changed;
            }
        };

        selects.forEach(select => {
            select.addEventListener("change", () => {
                updateTone(select);
                updateEditor();
            });
        });
        discard?.addEventListener("click", () => {
            selects.forEach(select => {
                select.value = select.dataset.originalValue;
                updateTone(select);
            });
            if (note) {
                note.value = "";
            }
            updateEditor();
        });
        updateEditor();
    });

    setSelectionMode(false);
    };

    const initializeLiveSearch = () => {
        const form = document.querySelector("[data-application-filter-form]");
        const search = form?.querySelector("#application-search");
        if (!form || !search) {
            return;
        }

        let debounceTimer;
        let activeRequest;

        search.addEventListener("input", () => {
            window.clearTimeout(debounceTimer);
            debounceTimer = window.setTimeout(async () => {
                activeRequest?.abort();
                activeRequest = new AbortController();

                const url = new URL(form.action || window.location.href, window.location.href);
                url.search = new URLSearchParams(new FormData(form)).toString();
                const currentContent = document.querySelector("[data-application-content]");
                currentContent?.setAttribute("aria-busy", "true");

                try {
                    const response = await fetch(url, {
                        headers: { "X-Requested-With": "fetch" },
                        signal: activeRequest.signal
                    });
                    if (!response.ok) {
                        throw new Error(`Application search failed with ${response.status}.`);
                    }

                    const nextPage = new DOMParser().parseFromString(await response.text(), "text/html");
                    const nextContent = nextPage.querySelector("[data-application-content]");
                    if (!nextContent || !currentContent) {
                        throw new Error("Application search results were unavailable.");
                    }

                    currentContent.replaceWith(nextContent);
                    window.history.replaceState({}, "", url);
                    initializeApplicationResults();
                    initializeLiveSearch();

                    const nextSearch = document.querySelector("#application-search");
                    nextSearch?.focus({ preventScroll: true });
                    nextSearch?.setSelectionRange(nextSearch.value.length, nextSearch.value.length);
                } catch (error) {
                    if (error.name !== "AbortError") {
                        window.location.assign(url);
                    }
                }
            }, 250);
        });
    };

    initializeApplicationResults();
    initializeLiveSearch();
})();
