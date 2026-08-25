(() => {
  "use strict";

  const DEFAULT_TRACKER_URL = "https://chit-thway-job-tracker-b9bpfvb5csccb5hb.australiaeast-01.azurewebsites.net";
  const form = document.getElementById("capture-form");
  const trackerUrlInput = document.getElementById("tracker-url");
  const status = document.getElementById("status");
  const submitButton = form.querySelector("button[type='submit']");

  chrome.storage.local.get({ trackerBaseUrl: DEFAULT_TRACKER_URL }, values => {
    trackerUrlInput.value = values.trackerBaseUrl;
  });

  form.addEventListener("submit", async event => {
    event.preventDefault();
    status.textContent = "";
    submitButton.disabled = true;

    try {
      const trackerBaseUrl = normalizeTrackerUrl(trackerUrlInput.value);
      await chrome.storage.local.set({ trackerBaseUrl });
      const [tab] = await chrome.tabs.query({ active: true, currentWindow: true });
      if (!tab?.id || !/^https?:/i.test(tab.url ?? "")) {
        throw new Error("Open a normal HTTP or HTTPS job advertisement first.");
      }

      const [{ result: capture }] = await chrome.scripting.executeScript({
        target: { tabId: tab.id },
        func: captureJobPage,
      });
      if (!capture || !capture.sourceText || capture.sourceText.length < 20) {
        throw new Error("This page did not expose enough readable job information.");
      }

      const payload = JSON.stringify(capture);
      const captureId = crypto.randomUUID();
      const storageKey = `capture:${captureId}`;
      await chrome.storage.session.set({
        [storageKey]: {
          payload,
          trackerBaseUrl,
          createdAt: Date.now(),
        },
      });

      try {
        const handoffUrl = chrome.runtime.getURL(
          `handoff.html?id=${encodeURIComponent(captureId)}`);
        await chrome.tabs.create({ url: handoffUrl });
      } catch (error) {
        await chrome.storage.session.remove(storageKey);
        throw error;
      }

      window.close();
    } catch (error) {
      status.textContent = error instanceof Error
        ? error.message
        : "The page could not be captured. Reload it and try again.";
      submitButton.disabled = false;
    }
  });

  function normalizeTrackerUrl(value) {
    let url;
    try {
      url = new URL(value.trim());
    } catch {
      throw new Error("Enter a complete tracker address.");
    }

    if (url.username || url.password || url.search || url.hash) {
      throw new Error("Use the tracker address without credentials, a query, or a fragment.");
    }

    const localHosts = new Set(["localhost", "127.0.0.1", "[::1]"]);
    if (url.protocol !== "https:" && !(url.protocol === "http:" && localHosts.has(url.hostname))) {
      throw new Error("Use HTTPS for a tracker that is not running on localhost.");
    }

    return `${url.origin}${url.pathname.replace(/\/$/, "")}`;
  }

  function captureJobPage() {
    const clean = (value, maximum = 2000) => {
      if (value === null || value === undefined) {
        return null;
      }

      const text = String(value).replace(/\s+/g, " ").trim();
      return text ? text.slice(0, maximum) : null;
    };
    const textFrom = (...selectors) => {
      for (const selector of selectors) {
        const element = document.querySelector(selector);
        const value = clean(element?.innerText ?? element?.textContent);
        if (value) {
          return value;
        }
      }

      return null;
    };
    const isVisible = element => {
      if (!element || element.closest('[aria-hidden="true"]')) {
        return false;
      }

      const style = window.getComputedStyle(element);
      return style.display !== "none"
        && style.visibility !== "hidden"
        && element.getClientRects().length > 0;
    };
    const elementFrom = (root, ...selectors) => {
      const scope = root ?? document;
      for (const selector of selectors) {
        for (const element of scope.querySelectorAll(selector)) {
          if (isVisible(element)) {
            return element;
          }
        }
      }

      return null;
    };
    const textFromElement = (root, ...selectors) => {
      const element = elementFrom(root, ...selectors);
      return clean(element?.innerText ?? element?.textContent);
    };
    const textFromElements = (root, ...selectors) => {
      const scope = root ?? document;
      const values = [];
      for (const selector of selectors) {
        for (const element of scope.querySelectorAll(selector)) {
          if (!isVisible(element)) continue;
          const value = clean(element.innerText ?? element.textContent);
          if (value) values.push(value);
        }
      }

      return clean(values.join(" "), 2000);
    };
    const meta = (...selectors) => {
      for (const selector of selectors) {
        const value = clean(document.querySelector(selector)?.getAttribute("content"));
        if (value) {
          return value;
        }
      }

      return null;
    };
    const cleanMultiline = (value, maximum = 50000) => {
      if (value === null || value === undefined) {
        return null;
      }

      const text = String(value)
        .replace(/\r\n?/g, "\n")
        .replace(/[\t ]+/g, " ")
        .replace(/ *\n */g, "\n")
        .replace(/\n{3,}/g, "\n\n")
        .trim();
      return text ? text.slice(0, maximum) : null;
    };
    const stripHtml = value => {
      if (!value) {
        return null;
      }

      const parsed = new DOMParser().parseFromString(String(value), "text/html");
      const blocks = [...parsed.body.querySelectorAll("h1, h2, h3, h4, h5, h6, p, li")]
        .map(element => clean(element.textContent, 5000))
        .filter(Boolean);
      return cleanMultiline(
        blocks.length > 0 ? blocks.join("\n\n") : parsed.body?.textContent,
        50000);
    };
    const findJobPosting = value => {
      if (!value || typeof value !== "object") {
        return null;
      }

      const types = Array.isArray(value["@type"]) ? value["@type"] : [value["@type"]];
      if (types.some(type => String(type).toLowerCase() === "jobposting")) {
        return value;
      }

      for (const child of Object.values(value)) {
        if (Array.isArray(child)) {
          for (const item of child) {
            const match = findJobPosting(item);
            if (match) {
              return match;
            }
          }
        } else if (child && typeof child === "object") {
          const match = findJobPosting(child);
          if (match) {
            return match;
          }
        }
      }

      return null;
    };
    const schemaPosting = (() => {
      for (const script of document.querySelectorAll('script[type="application/ld+json"]')) {
        try {
          const match = findJobPosting(JSON.parse(script.textContent ?? ""));
          if (match) {
            return match;
          }
        } catch {
          // Ignore malformed page-owned JSON-LD and continue with rendered fields.
        }
      }

      return null;
    })();
    const organizationName = organization => {
      if (Array.isArray(organization)) {
        return organizationName(organization[0]);
      }

      return clean(typeof organization === "string" ? organization : organization?.name);
    };
    const addressText = location => {
      const first = Array.isArray(location) ? location[0] : location;
      const address = first?.address ?? first;
      if (typeof address === "string") {
        return clean(address, 300);
      }

      if (!address || typeof address !== "object") {
        return null;
      }

      return clean([
        address.streetAddress,
        address.addressLocality,
        address.addressRegion,
        address.postalCode,
        typeof address.addressCountry === "object" ? address.addressCountry?.name : address.addressCountry,
      ].filter(Boolean).join(", "), 300);
    };
    const identifierText = identifier => {
      const first = Array.isArray(identifier) ? identifier[0] : identifier;
      return clean(typeof first === "string" ? first : first?.value ?? first?.name, 200);
    };
    const salaryText = salary => {
      const first = Array.isArray(salary) ? salary[0] : salary;
      if (!first || typeof first !== "object") {
        return clean(first, 500);
      }

      const value = first.value ?? first;
      const currency = clean(first.currency ?? value.currency, 12);
      const minimum = value.minValue ?? value.value;
      const maximum = value.maxValue;
      const unit = clean(value.unitText, 30);
      const amount = minimum !== undefined && maximum !== undefined
        ? `${minimum} - ${maximum}`
        : minimum ?? maximum;
      return clean([currency, amount, unit ? `/ ${unit}` : null].filter(Boolean).join(" "), 500);
    };
    const employmentText = value => clean(
      (Array.isArray(value) ? value : [value])
        .filter(Boolean)
        .map(item => String(item).replace(/_/g, " ").toLowerCase().replace(/\b\w/g, letter => letter.toUpperCase()))
        .join(", "),
      200);
    const matchedLabel = (value, pattern, maximum = 200) => {
      const match = String(value ?? "").match(pattern);
      return clean(match?.[0]?.replace(/-/g, " "), maximum);
    };


    const isIndeed = window.location.hostname === "indeed.com"
      || window.location.hostname.startsWith("indeed.")
      || window.location.hostname.includes(".indeed.");
    const indeedTitle = isIndeed
      ? clean(textFrom('[data-testid="jobsearch-JobInfoHeader-title"]')
        ?.replace(/\s*-\s*job post\s*$/i, ""), 200)
      : null;
    const indeedCompany = isIndeed
      ? textFrom('[data-testid="inlineHeader-companyName"]')
      : null;
    const indeedLocation = isIndeed
      ? textFrom(
        '[data-testid="inlineHeader-companyLocation"]',
        "#jobLocationText")
      : null;
    const indeedSalary = isIndeed
      ? textFrom(
        '#jobDetailsSection [aria-label="Pay"] [data-testid="list-item"]',
        "#salaryInfoAndJobType > span:first-child")
      : null;
    const indeedEmploymentType = isIndeed
      ? textFrom(
        '#jobDetailsSection [aria-label="Job type"] [data-testid="list-item"]',
        "#salaryInfoAndJobType > span:nth-child(2)")
          ?.replace(/^\s*-\s*/, "") ?? null
      : null;
    const indeedJobReference = isIndeed
      ? clean(new URL(window.location.href).searchParams.get("vjk"), 200)
      : null;


    const isLinkedIn = window.location.hostname === "linkedin.com"
      || window.location.hostname.endsWith(".linkedin.com");
    const linkedInDetail = isLinkedIn
      ? elementFrom(
        document,
        ".jobs-search__job-details--container",
        ".jobs-search__job-details",
        ".scaffold-layout__detail",
        "[data-job-details]")
      : null;
    const linkedInTitle = isLinkedIn
      ? textFromElement(
        linkedInDetail ?? document,
        ".job-details-jobs-unified-top-card__job-title h1",
        ".job-details-jobs-unified-top-card__job-title",
        ".jobs-unified-top-card__job-title",
        'h1 a[href*="/jobs/view/"]',
        "h1")
      : null;
    const linkedInCompany = isLinkedIn
      ? textFromElement(
        linkedInDetail ?? document,
        ".job-details-jobs-unified-top-card__company-name a",
        ".job-details-jobs-unified-top-card__company-name",
        ".jobs-unified-top-card__company-name a",
        ".jobs-unified-top-card__company-name")
      : null;
    const linkedInHeaderMeta = isLinkedIn
      ? textFromElement(
        linkedInDetail ?? document,
        ".job-details-jobs-unified-top-card__primary-description-container",
        ".jobs-unified-top-card__primary-description")
      : null;
    const linkedInLocation = clean(linkedInHeaderMeta?.split("·")[0], 300);
    const linkedInInsightText = isLinkedIn
      ? textFromElements(
        linkedInDetail ?? document,
        "[data-test-job-type]",
        ".job-details-jobs-unified-top-card__job-insight",
        ".job-details-preferences-and-skills__pill")
      : null;
    const linkedInEmploymentType = matchedLabel(
      linkedInInsightText,
      /\b(?:full[- ]time|part[- ]time|contract|temporary|casual|internship|volunteer)\b/i);
    const linkedInWorkplaceMode = matchedLabel(
      `${linkedInInsightText ?? ""} ${linkedInHeaderMeta ?? ""}`,
      /\b(?:remote|hybrid|on[- ]site)\b/i,
      100);
    const linkedInJobReference = isLinkedIn
      ? clean(
        new URL(window.location.href).searchParams.get("currentJobId")
          ?? window.location.pathname.match(/\/jobs\/view\/(\d+)/)?.[1],
        200)
      : null;
    const roleTitle = clean(schemaPosting?.title, 200)
      ?? indeedTitle
      ?? linkedInTitle
      ?? textFrom('[data-automation="job-detail-title"]', '[itemprop="title"]', "main h1", "h1");
    const companyName = organizationName(schemaPosting?.hiringOrganization)
      ?? indeedCompany
      ?? linkedInCompany
      ?? textFrom('[data-automation="advertiser-name"]', '[itemprop="hiringOrganization"] [itemprop="name"]', '[itemprop="hiringOrganization"]');
    const companyLocation = addressText(schemaPosting?.jobLocation)
      ?? indeedLocation
      ?? linkedInLocation
      ?? textFrom('[data-automation="job-detail-location"]', '[itemprop="jobLocation"]');
    const employmentType = employmentText(schemaPosting?.employmentType)
      ?? indeedEmploymentType
      ?? linkedInEmploymentType
      ?? textFrom('[data-automation="job-detail-work-type"]', '[itemprop="employmentType"]');
    const salary = salaryText(schemaPosting?.baseSalary)
      ?? indeedSalary
      ?? textFrom('[data-automation="job-detail-salary"]', '[itemprop="baseSalary"]');
    const workplaceMode = clean(
      String(schemaPosting?.jobLocationType ?? "").toUpperCase() === "TELECOMMUTE"
        ? "Remote"
        : linkedInWorkplaceMode,
      100);
    const closingDate = clean(schemaPosting?.validThrough, 100)
      ?? textFrom('[data-automation="job-detail-closing-date"]', '[itemprop="validThrough"]');
    const jobReference = identifierText(schemaPosting?.identifier)
      ?? indeedJobReference
      ?? linkedInJobReference;
    const sourceSite = meta('meta[property="og:site_name"]', 'meta[name="application-name"]')
      ?? clean(window.location.hostname, 200);
    const linkedInDescriptionElement = isLinkedIn
      ? elementFrom(
        linkedInDetail ?? document,
        "#job-details",
        ".jobs-description-content__text",
        ".jobs-box__html-content",
        ".jobs-description__content")
      : null;
    const renderedDescriptionElement = linkedInDescriptionElement
      ?? document.querySelector('#jobDescriptionText, [data-automation="jobAdDetails"], [itemprop="description"], main article');
    const description = stripHtml(schemaPosting?.description)
      ?? cleanMultiline(
        renderedDescriptionElement?.innerText ?? renderedDescriptionElement?.textContent,
        50000);

    const sourceParts = [];
    const add = (label, value) => {
      if (value) {
        sourceParts.push(`${label}: ${value}`);
      }
    };
    add("Job title", roleTitle);
    add("Company", companyName);
    add("Location", companyLocation);
    add("Work arrangement", workplaceMode);
    add("Source site", sourceSite);
    add("Job reference", jobReference);
    add("Salary", salary);
    add("Employment type", employmentType);
    add("Closing date", closingDate);
    if (description) {
      sourceParts.push("", description);
    }

    return {
      version: 1,
      pageUrl: window.location.href,
      roleTitle,
      companyName,
      companyLocation,
      workplaceMode,
      sourceSite,
      jobReference,
      salaryText: salary,
      employmentType,
      closingDate,
      descriptionText: description,
      sourceText: sourceParts.join("\n").slice(0, 60000),
    };
  }
})();
