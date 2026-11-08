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

function matchedKey(field) {
  const label = normalize(field.label.replace(/\((?:required|optional|\s*(?:mm|yyyy)\s*[-/]\s*(?:mm|yyyy)\s*)\)/gi, ""))
    .replace(/\s+(?:required|optional)$/, "");
  if (field.customPicker) return null;
  const context = normalize(field.context);
  const autocomplete = field.autocomplete.toLowerCase().split(/\s+/);
  if (/\b(referees?|references?|emergency contacts?|login|sign in|sign up|newsletter|billing|shipping|consent|declaration|gender|disability|verification|password|captcha|payment)\b/.test(`${label} ${context}`)
      || autocomplete.some(token => ["shipping", "billing", "username", "one-time-code"].includes(token))) {
    return null;
  }

  const mapping = field.group ? historyFields[field.group] : contactFields;
  const metadata = [field.name.split(/[.\[\]]/).filter(Boolean).at(-1), field.id];
  const names = [label, ...metadata.map(normalize)];
  const semantic = field.group ? null : autocomplete.map(token => autocompleteKeys.get(token)).find(Boolean);
  return mapping.get(label) ?? semantic ?? names.map(name => mapping.get(name)).find(Boolean) ?? null;
}

function equivalentSelect(field, values) {
  const keys = new Set(values.filter(Boolean).map(normalize));
  const matches = field.options.filter(option =>
    keys.has(normalize(option.value)) || keys.has(normalize(option.label)));
  return matches.length === 1 ? matches[0].value : null;
}

function formattedValue(field, key, value, country) {
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
    entries.push({ field, key: matchedKey(field) });
    forms.set(field.formKey, entries);
  }

  const candidates = [...forms.values()].filter(entries => {
    if (entries.some(({ field }) => field.hasPassword)) return false;
    const keys = entries.filter(({ field }) => !field.group).map(({ key }) => key);
    const contactPair = keys.includes("email") && keys.some(key => ["givenName", "familyName", "fullName"].includes(key));
    const distinctContactFields = new Set(keys.filter(Boolean)).size;
    return contactPair || entries.some(({ field, key }) => key && (field.group || field.applicationSection
      || field.applicationContext && (field.focused || distinctContactFields >= 2)));
  });
  const focused = candidates.filter(entries => entries.some(({ field }) => field.focused));
  const selected = focused.length === 1 ? focused[0] : candidates.length === 1 ? candidates[0] : null;
  if (!selected) {
    return { writes: [], review: [], reason: candidates.length ? "Click a field in the application, then try Fill again." : "No supported application fields found." };
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
  for (const { field, key } of selected) {
    if (!field.blank) continue;
    const source = field.group ? groupSources.get(field.groupKey) : profile;
    const value = key && source
      ? formattedValue(field, key, field.group ? source[key] : contactValue(key, source, splitAddress), profile.country)
      : null;
    if (value !== null) {
      writes.push({ index: field.index, value });
    } else if (field.label && field.type !== "checkbox") {
      review.push(field.label);
    }
  }
  return { writes, review };
}
