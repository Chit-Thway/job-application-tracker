(() => {
  const demo = document.querySelector("[data-extension-demo]");
  if (!demo) return;

  const progress = document.querySelector("[data-demo-progress]");
  const instruction = document.querySelector("[data-demo-instruction]");
  const explanation = document.querySelector("[data-demo-explanation]");
  const toolbarButton = demo.querySelector("[data-demo-action='open']");
  const jobPage = demo.querySelector("[data-demo-stage='job']");
  const popup = demo.querySelector("[data-demo-stage='popup']");
  const review = demo.querySelector("[data-demo-stage='review']");
  const saved = demo.querySelector("[data-demo-stage='saved']");

  const steps = {
    open: {
      progress: "Step 1 of 3",
      instruction: "Click the highlighted JT extension icon.",
      explanation: "The job advertisement is open. The extension reads this page only after you choose to capture it.",
    },
    capture: {
      progress: "Step 2 of 3",
      instruction: "Click ‘Capture this tab and review’.",
      explanation: "The extension extracts the role and company from this fictional job page.",
    },
    save: {
      progress: "Step 3 of 3",
      instruction: "Check the details, then save the application.",
      explanation: "A real capture opens an editable draft. You choose when it becomes an application.",
    },
    replay: {
      progress: "Done",
      instruction: "Your application is saved!",
      explanation: "Only this demonstration changed. No real application was created.",
    },
  };

  let expectedAction = "open";

  function showStep(action) {
    const step = steps[action];
    expectedAction = action;
    progress.textContent = step.progress;
    instruction.textContent = step.instruction;
    explanation.textContent = step.explanation;

    toolbarButton.hidden = action !== "open";
    jobPage.hidden = action === "save" || action === "replay";
    popup.hidden = action !== "capture";
    review.hidden = action !== "save";
    saved.hidden = action !== "replay";
  }

  demo.addEventListener("click", event => {
    const button = event.target.closest("[data-demo-action]");
    if (!button || button.dataset.demoAction !== expectedAction) return;

    const nextStep = { open: "capture", capture: "save", save: "replay", replay: "open" };
    showStep(nextStep[expectedAction]);
  });
})();
