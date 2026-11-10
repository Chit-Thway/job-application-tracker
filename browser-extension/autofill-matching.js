const normalize = value => String(value ?? "")
  .replace(/([a-z0-9])([A-Z])/g, "$1 $2")
  .toLowerCase().replace(/[^\p{L}\p{N}]+/gu, " ").trim();

const aliases = rules => new Map(Object.entries(rules).flatMap(([key, names]) =>
  names.split("|").map(name => [name, key])));

const contactFields = aliases({
  fullName: "full name|your name|legal name|applicant name|candidate name",
  givenName: "first name|given name|given names|legal first name|fname",
  familyName: "last name|surname|family name|legal last name|lname",
  preferredName: "preferred name|preferred first name",
  email: "email|e mail|email address|e mail address|personal email|applicant email",
  phone: "phone|phone number|telephone|telephone number|mobile|mobile number|contact number",
  addressLine1: "street address|address line 1|address 1|street",
  addressLine2: "address line 2|address 2|apartment suite unit",
  address: "address",
  fullAddress: "full address|complete address|residential address|mailing address",
  city: "city|suburb|town|locality|city or suburb|suburb or city|location city|current location",
  region: "state|province|region|state province|state or territory|state region",
  postcode: "postcode|postal code|post code|zip|zip code",
  country: "country|country region|country of residence",
  linkedIn: "linkedin|linkedin url|linkedin profile|linkedin profile url|linked in|linked in url",
  gitHub: "github|github url|github profile|github profile url|git hub|git hub url",
  portfolio: "portfolio|portfolio url|portfolio website|personal website|website url",
  noticePeriod: "notice period|current notice period|what is your notice period",
  availability: "availability|start availability|when can you start|when are you available to start",
  currentCompany: "current company|current employer",
  currentTitle: "current job title|current position",
});

const autocompleteKeys = new Map(Object.entries({
  name: "fullName", "given-name": "givenName", "family-name": "familyName",
  nickname: "preferredName", email: "email", tel: "phone",
  "street-address": "streetAddress", "address-line1": "addressLine1",
  "address-line2": "addressLine2", "address-level2": "city",
  "address-level1": "region", "postal-code": "postcode", country: "country",
  "country-name": "country",
}));

const historyFields = {
  experience: aliases({
    employer: "employer|company|company name|organization|organisation|employer name",
    title: "title|job title|position|position title|role|role title",
    location: "location|job location|work location",
    startMonth: "start date|from|from date|start month|start year|from month|from year",
    endMonth: "end date|to|to date|end month|end year|to month|to year",
    current: "currently work here|i currently work here|i currently work in this role|current job",
    responsibilities: "description|responsibilities|job description|role description|responsibilities and achievements|duties",
  }),
  education: aliases({
    institution: "school|university|institution|college|school or university|school name",
    qualification: "degree|qualification|qualification name|degree type",
    fieldOfStudy: "field of study|major|discipline",
    startMonth: "start date|from|start month|start year",
    endMonth: "end date|to|end month|end year|graduation date|graduation year",
  }),
  certifications: aliases({
    name: "name|certification|certification name|certificate name|licence name|license name",
    issuer: "issuer|issuing organization|issuing organisation|issuing authority",
    credentialId: "credential id|certificate number|license number|licence number",
    licenceClass: "licence class|license class|licence category|license category|driver s licence class|driver s license class",
    credentialUrl: "credential url|certificate url",
    issuedMonth: "issued date|issue date|issued month|date issued",
    expiryMonth: "expiry date|expiration date|expiry month|expiration month",
  }),
};

const regions = [
  ["WA", "Western Australia"], ["NT", "Northern Territory"], ["QLD", "Queensland"],
  ["NSW", "New South Wales"], ["VIC", "Victoria"], ["SA", "South Australia"],
  ["TAS", "Tasmania"], ["ACT", "Australian Capital Territory"],
];
const monthNames = ["January", "February", "March", "April", "May", "June",
  "July", "August", "September", "October", "November", "December"];

function questionLabel(label) {
  return normalize(label.replace(/\((?:required|optional|\s*(?:mm|yyyy)\s*[-/]\s*(?:mm|yyyy)\s*)\)/gi, ""))
    .replace(/\s+(?:required|optional)$/, "");
}

function restricted(field) {
  const context = normalize(`${field.label} ${field.context}`);
  return /\b(referees?|references?|emergency contacts?|login|sign in|sign up|newsletter|billing|shipping|consent|declaration|gender|pronouns|ethnicity|race|sexual orientation|disability|veteran|criminal|police check|verification|password|captcha|payment|passport)\b/.test(context) ||
    field.autocomplete.toLowerCase().split(/\s+/).some(token => ["shipping", "billing", "username", "one-time-code"].includes(token));
}

function matchedKey(field) {
  const label = questionLabel(field.label);
  if (field.customPicker) return null;
  const autocomplete = field.autocomplete.toLowerCase().split(/\s+/);
  if (restricted(field) || field.type === "file") return null;

  const mapping = field.group ? historyFields[field.group] : contactFields;
  const metadata = [field.name.split(/[.\[\]]/).filter(Boolean).at(-1), field.id];
  const names = [label, ...metadata.map(normalize)];
  const semantic = field.group ? null : autocomplete.map(token => autocompleteKeys.get(token)).find(Boolean);
  return mapping.get(label) ?? semantic ?? names.map(name => mapping.get(name)).find(Boolean) ?? null;
}

const workCountries = [
  ["AU", /\baustralia\b/], ["NZ", /\bnew zealand\b/], ["CA", /\bcanada\b/],
  ["GB", /\b(?:united kingdom|uk)\b/], ["US", /\b(?:united states|usa|u s a|u s)\b/],
];

function screeningAnswer(label, profile) {
  const countries = workCountries.filter(([, pattern]) => pattern.test(label));
  if (countries.length !== 1 || /\b(without|unrestricted|restricted|not|citizen|residency|conditions|limitations|full time|part time)\b/.test(label)) return null;
  const rows = (profile.workPermissions ?? []).filter(row => row.countryCode === countries[0][0]);
  if (rows.length !== 1) return null;
  const { authorized, sponsorshipNow, sponsorshipLater } = rows[0];
  const target = label.replace(countries[0][1], "country");
  if (/\bsponsorship\b/.test(target)) {
    if (!/^(?:do you(?: now or will you in the future| now| currently)?|will you(?: in the future)?) (?:require|need) (?:(?:visa|employment visa|employer) )?sponsorship(?: for employment visa status)? (?:to work )?(?:in|for) (?:the )?country(?: now| currently| in the future)?$/.test(target)) return null;
    if (/\b(later|future)\b/.test(label)) {
      if (!/\b(now|currently|today)\b/.test(label)) return sponsorshipLater;
      if (sponsorshipNow === true || sponsorshipLater === true) return true;
      return sponsorshipNow === false && sponsorshipLater === false ? false : null;
    }
    return /\b(now|currently|today)\b/.test(label) ? sponsorshipNow : null;
  }
  if (/^(?:do you (?:currently )?have (?:the )?(?:legal )?(?:right|authorization|authorisation) to work|are you (?:currently )?(?:legally )?(?:authorized|authorised|eligible) to work) (?:in|within) (?:the )?country$/.test(target)) return authorized;
  return null;
}

function yesNoValue(field, answer) {
  if (typeof answer !== "boolean") return null;
  const choice = answer ? "yes" : "no";
  if (field.type === "radio") return normalize(field.radioChoice) === choice ? true : null;
  return field.tag === "SELECT" ? equivalentSelect(field, [choice]) : null;
}

export function attachmentKind(field) {
  if (field.type !== "file" || restricted(field)) return null;
  const label = questionLabel(field.label);
  if (/^(?:(?:attach|upload) (?:your |a )?)?(?:r[ée]sum[ée]|cv|curriculum vitae)$/.test(label)) return "resume";
  if (/^(?:(?:attach|upload) (?:your |a )?)?cover letter$/.test(label)) return "coverLetter";
  if (/^(?:(?:attach|upload) (?:your |a )?)?portfolio(?: file)?$/.test(label)) return "portfolio";
  return null;
}

function suggestedAnswer(field, label, profile) {
  if (field.tag === "SELECT" || field.type === "radio") {
    if (/\b(?:open to|willing to) relocat/.test(label)) return yesNoValue(field, profile.openToRelocation);
    if (/\b(?:open to|willing to) (?:working in (?:a )?|work in (?:a )?)?hybrid\b/.test(label)) return yesNoValue(field, profile.openToHybridWork);
  }
  if (field.tag !== "TEXTAREA" && field.type !== "text") return null;
  if (/\bsalary (?:expectation|expectations)\b/.test(label) && !/\b(current|previous)\b/.test(label)) {
    return profile.salaryExpectation || null;
  }
  const saved = (profile.savedAnswers ?? []).filter(entry => questionLabel(entry.question ?? "") === label);
  return saved.length === 1 ? saved[0].answer : null;
}

function equivalentSelect(field, values) {
  const keys = new Set(values.filter(Boolean).map(normalize));
  const matches = field.options.filter(option =>
    keys.has(normalize(option.value)) || keys.has(normalize(option.label)));
  return matches.length === 1 ? matches[0].value : null;
}

function formattedValue(field, key, value, country) {
  if (field.type === "file" || field.type === "radio") return null;
  if (typeof value === "boolean") {
    return key === "current" && field.type === "checkbox" && value ? true : null;
  }
  if (!value || field.type === "checkbox") return null;
  if (key === "postcode" && field.type === "number" && /^0\d/.test(value)) return null;

  const date = key.endsWith("Month") ? /^(\d{4})-(\d{2})$/.exec(value) : null;
  let alternatives = [value];
  if (date) {
    const [, year, month] = date;
    const hint = `${field.label} ${field.placeholder}`.toLowerCase();
    const monthName = monthNames[Number(month) - 1];
    if (field.type === "date") return null;
    if (field.type === "month") return value;
    if (field.tag === "SELECT" && !/year/.test(hint)) {
      alternatives = [value, month, String(Number(month)), monthName, monthName.slice(0, 3)];
    } else if (/\byyyy\s*[-/]\s*mm\b/.test(hint)) {
      return hint.includes("yyyy/mm") ? `${year}/${month}` : value;
    } else if (/\bmm\s*[-/]\s*yyyy\b/.test(hint)) {
      return hint.includes("mm-yyyy") ? `${month}-${year}` : `${month}/${year}`;
    } else if (/\byear\b|\byyyy\b/.test(hint)) {
      alternatives = [year];
    } else if (/\bmonth\b/.test(hint)) {
      alternatives = [value];
    } else {
      return null;
    }
  }

  if (field.tag === "SELECT") {
    if (key === "country" && ["au", "aus", "australia"].includes(normalize(value))) {
      alternatives = ["AU", "AUS", "Australia"];
    }
    if (key === "region" && ["au", "aus", "australia"].includes(normalize(country))) {
      alternatives = regions.find(region => region.some(item => normalize(item) === normalize(value))) ?? alternatives;
    }
    return equivalentSelect(field, alternatives);
  }
  if (field.maxLength > 0 && alternatives[0].length > field.maxLength) return null;
  return alternatives[0];
}

function contactValue(key, profile, splitAddress) {
  if (key === "currentCompany" || key === "currentTitle") {
    const roles = profile.experience?.filter(role => role.current);
    return roles?.length === 1 ? roles[0][key === "currentCompany" ? "employer" : "title"] : null;
  }
  if (key === "fullName") return profile.fullName || [profile.givenName, profile.familyName].filter(Boolean).join(" ");
  if (key === "streetAddress") return [profile.addressLine1, profile.addressLine2].filter(Boolean).join(", ");
  if (key === "address" && splitAddress) return profile.addressLine1;
  if (key === "fullAddress" || key === "address") {
    const locality = [profile.city, profile.region, profile.postcode].filter(Boolean).join(" ");
    return [profile.addressLine1, profile.addressLine2, locality, profile.country].filter(Boolean).join(", ");
  }
  return profile[key];
}

export function planAutofill(fields, profile) {
  const forms = new Map();
  for (const field of fields) {
    const entries = forms.get(field.formKey) ?? [];
    const label = questionLabel(field.label);
    const eligible = !field.group && !field.customPicker && !restricted(field) && label;
    const screen = eligible ? yesNoValue(field, screeningAnswer(label, profile)) : null;
    const suggestion = eligible ? suggestedAnswer(field, label, profile) : null;
    entries.push({ field, key: matchedKey(field), screen, suggestion });
    forms.set(field.formKey, entries);
  }

  const candidates = [...forms.values()].filter(entries => {
    if (entries.some(({ field }) => field.hasPassword)) return false;
    const keys = entries.filter(({ field }) => !field.group).map(({ key }) => key);
    const contactPair = keys.includes("email") && keys.some(key => ["givenName", "familyName", "fullName"].includes(key));
    const distinctContactFields = new Set(keys.filter(Boolean)).size;
    return contactPair || entries.some(({ field, key }) => key && (field.group || field.applicationSection
      || field.applicationContext && (field.focused || distinctContactFields >= 2))) ||
      entries.some(({ field, screen, suggestion }) => field.applicationContext &&
        (screen !== null || suggestion !== null || attachmentKind(field) !== null));
  });
  const focused = candidates.filter(entries => entries.some(({ field }) => field.focused));
  const selected = focused.length === 1 ? focused[0] : candidates.length === 1 ? candidates[0] : null;
  if (!selected) {
    return { writes: [], review: [], suggestions: [], attachments: [], reason: candidates.length ? "Click a field in the application, then try Fill again." : "No supported application fields found." };
  }

  const groups = new Map();
  for (const entry of selected.filter(({ field }) => field.group)) {
    const id = entry.field.groupKey;
    const group = groups.get(id) ?? [];
    group.push(entry);
    groups.set(id, group);
  }

  const groupSources = new Map();
  for (const kind of Object.keys(historyFields)) {
    const sections = [...groups.values()].filter(group => group[0].field.group === kind);
    const entries = profile[kind] ?? [];
    for (const section of sections) {
      const anchorKeys = kind === "experience" ? ["employer", "title"] :
        kind === "education" ? ["institution", "qualification"] : ["name", "issuer"];
      const anchors = section.filter(({ field, key }) => anchorKeys.includes(key) && field.anchorValue);
      const matching = anchors.length ? entries.filter(entry => anchors.every(({ field, key }) =>
        normalize(field.anchorValue) === normalize(entry[key]))) : [];
      const index = section[0].field.groupIndex;
      const hasExistingValues = section.some(({ field }) => !field.blank);
      const source = anchors.length ? (matching.length === 1 ? matching[0] : null) :
        hasExistingValues ? null : Number.isInteger(index) ? entries[index] : sections.length === 1 ? entries[0] : null;
      const keys = section.map(({ key }) => key).filter(key => key && !key.endsWith("Month"));
      if (source && new Set(keys).size === keys.length) groupSources.set(section[0].field.groupKey, source);
    }
  }
  const assigned = [...groupSources.values()];
  for (const [id, source] of groupSources) {
    if (assigned.filter(entry => entry === source).length > 1) groupSources.delete(id);
  }

  const splitAddress = selected.some(({ key }) => ["city", "region", "postcode"].includes(key));
  const writes = [];
  const review = [];
  const suggestions = [];
  const attachments = [];
  const coveredRadioQuestions = new Set(selected.filter(({ field, screen }) => field.type === "radio" && screen !== null).map(({ field }) => field.label));
  for (const { field, key, screen, suggestion } of selected) {
    if (!field.blank) continue;
    const kind = attachmentKind(field);
    if (kind) attachments.push({ kind, index: field.index });
    if (screen !== null) {
      writes.push({ index: field.index, value: screen });
      continue;
    }
    if (suggestion !== null && (typeof suggestion !== "string" || field.maxLength < 1 || suggestion.length <= field.maxLength || field.tag === "SELECT")) {
      const editable = field.type === "text" || field.tag === "TEXTAREA";
      const display = field.type === "radio" ? field.radioChoice : field.tag === "SELECT" ? field.options.find(option => option.value === suggestion)?.label : suggestion;
      suggestions.push({ index: field.index, label: field.label, value: suggestion, display, editable, maxLength: field.maxLength });
      continue;
    }
    const source = field.group ? groupSources.get(field.groupKey) : profile;
    const value = key && source
      ? formattedValue(field, key, field.group ? source[key] : contactValue(key, source, splitAddress), profile.country)
      : null;
    if (value !== null) {
      writes.push({ index: field.index, value });
    } else if (field.label && field.type !== "checkbox" && !coveredRadioQuestions.has(field.label)) {
      review.push(field.label);
    }
  }
  const suggestedLabels = new Set(suggestions.map(suggestion => suggestion.label));
  return { writes, review: review.filter(label => !suggestedLabels.has(label)), suggestions, attachments };
}
