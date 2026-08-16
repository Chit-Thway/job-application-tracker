# WCAG 2.2 AA verification

This checklist follows the [W3C WCAG 2.2 Recommendation](https://www.w3.org/TR/WCAG22/) and its [new 2.2 criteria](https://www.w3.org/WAI/standards-guidelines/wcag/new-in-22/). It targets the tracker's critical private and public journeys rather than claiming certification from automated tests alone.

## Automated coverage

`tests/JobTracker.BrowserTests` checks the login, manual-create, Saved, status-update, dashboard, and demo journeys in Chromium. Every visited page must have:

- an English language declaration and non-empty document title;
- one main landmark and one level-one heading;
- a keyboard skip link to the main content;
- unique element IDs;
- an accessible label for every non-hidden form control;
- alternative text on every image;
- no horizontal overflow in the 390-by-844 demo viewport;
- no mutation controls in the public demo.

Motion honours `prefers-reduced-motion`. Links, buttons, inputs, selects, text areas, summaries, and programmatic focus targets use a visible focus ring. Validation summaries announce errors; forms only move focus to a summary after server-side validation fails.

## Manual acceptance checklist

Run this in current Chrome or Edge at 200% browser zoom, once in light mode and once in dark mode.

1. On `/account/login`, press `Tab`. The first focusable item is **Skip to main content**. Press Enter and confirm focus moves to the main region.
2. Complete login without a mouse. Focus remains visible and is never trapped in the menu, theme switch, forms, details sections, job-description dialog, or confirmation pages.
3. Create an application with an intentionally empty role title. Confirm the error is announced, associated with the field, and the entered non-secret text remains available for correction.
4. From application details, operate Saved, status history, contacts, tasks, appointments, description popout, and delete confirmation by keyboard.
5. At 200% zoom and at approximately 390 CSS pixels, check login, dashboard, Applications card and list views, application details, Action Centre, Settings, and all `/demo` routes. No required control or text should be clipped or overlap.
6. With a screen reader, confirm the page title, heading order, landmarks, current navigation item, field names, validation errors, status messages, counts, and read-only demo notice are understandable without visual position.
7. Verify text, focus rings, control boundaries, status badges, errors, and chart labels remain distinguishable in both themes. Do not rely on colour alone to identify Saved, overdue, selected, or deletion-scheduled states.
8. Confirm pointer targets used in the main workflows are comfortably selectable and that no action requires dragging or precise movement.

Record the browser, screen reader, operating system, date, failures, and retest result in the pull request. Any failure affecting task completion blocks launch until fixed or explicitly documented.
