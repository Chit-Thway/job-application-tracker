importScripts("tracker-config.js");

const tokenKey = "autofill:access";
const connectionResultKey = "autofill:connectionResult";
let pendingConnection;

chrome.runtime.onMessage.addListener((message, sender, respond) => {
  if (message?.type !== "autofill:connect" || sender.id !== chrome.runtime.id
      || sender.url !== chrome.runtime.getURL("popup.html")) return;

  pendingConnection ??= connect().finally(() => { pendingConnection = undefined; });
  respond(true);
});

async function connect() {
  await chrome.storage.session.set({ [connectionResultKey]: "working" });
  let result = "failed";
  try {
    result = await authorizeProfile();
  } catch {
    // Chrome also rejects this request when the sign-in window is closed.
  }
  await chrome.storage.session.set({ [connectionResultKey]: result });
}

function randomToken() {
  const random = crypto.getRandomValues(new Uint8Array(32));
  return btoa(String.fromCharCode(...random)).replaceAll("+", "-").replaceAll("/", "_").replace(/=+$/, "");
}

async function authorizeProfile() {
  const state = randomToken();
  const verifier = randomToken();
  const hash = await crypto.subtle.digest("SHA-256", new TextEncoder().encode(verifier));
  const challenge = [...new Uint8Array(hash)].map(byte => byte.toString(16).padStart(2, "0")).join("");
  const request = new URL("/extension/profile/authorize", trackerOrigin);
  request.searchParams.set("extensionId", chrome.runtime.id);
  request.searchParams.set("challenge", challenge);
  request.searchParams.set("state", state);

  const expected = new URL(chrome.identity.getRedirectURL("autofill"));
  const callback = new URL(await chrome.identity.launchWebAuthFlow({ url: request.href, interactive: true }));
  if (callback.origin !== expected.origin || callback.pathname !== expected.pathname || callback.search) return "failed";
  const result = new URLSearchParams(callback.hash.slice(1));
  if (result.get("state") !== state) return "failed";
  if (result.get("error") === "profile_required") return "profile-required";
  const code = result.get("code");
  if (!code || !/^[A-Za-z0-9_-]{43}$/.test(code)) return "failed";

  const response = await fetch(new URL("/extension/profile/exchange", trackerOrigin), {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ code, verifier }),
    cache: "no-store",
    credentials: "omit",
    redirect: "error",
    signal: AbortSignal.timeout(8000),
  });
  if (!response.ok) return "failed";
  const { token } = await response.json();
  if (typeof token !== "string" || !/^[A-Za-z0-9_-]{43}$/.test(token)) return "failed";
  await chrome.storage.session.set({ [tokenKey]: token });
  return "connected";
}
