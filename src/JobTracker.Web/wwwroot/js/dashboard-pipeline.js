(() => {
  "use strict";

  const chart = document.querySelector("[data-pipeline-chart]");
  if (!chart) {
    return;
  }

  const total = chart.querySelector("[data-pipeline-total]");
  const tooltip = chart.querySelector("[data-pipeline-tooltip]");
  const tooltipLabel = chart.querySelector("[data-pipeline-tooltip-label]");
  const tooltipCount = chart.querySelector("[data-pipeline-tooltip-count]");
  const segments = chart.querySelectorAll("[data-pipeline-label]");

  const show = segment => {
    const count = Number(segment.dataset.pipelineCount ?? 0);
    tooltipLabel.textContent = segment.dataset.pipelineLabel ?? "Pipeline stage";
    tooltipCount.textContent = `${count} ${count === 1 ? "application" : "applications"}`;
    total.hidden = true;
    tooltip.hidden = false;
    chart.classList.add("is-inspecting");
    segment.classList.add("is-active");
  };

  const reset = segment => {
    segment.classList.remove("is-active");
    tooltip.hidden = true;
    total.hidden = false;
    chart.classList.remove("is-inspecting");
  };

  for (const segment of segments) {
    segment.addEventListener("pointerenter", () => show(segment));
    segment.addEventListener("pointerleave", () => {
      if (!segment.matches(":focus")) {
        reset(segment);
      }
    });
    segment.addEventListener("focus", () => show(segment));
    segment.addEventListener("blur", () => {
      if (!segment.matches(":hover")) {
        reset(segment);
      }
    });
  }
})();
