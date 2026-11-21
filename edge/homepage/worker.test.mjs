import assert from "node:assert/strict";
import { test } from "node:test";
import worker from "./worker.mjs";

const publicOrigin = "https://myjobtracker.com.au";
const previewOrigin = "https://preview.example.test";
const assetHeaders = { "X-Homepage-Video-Length": "8", "Content-Type": "video/webm" };

test("anonymous homepage serves its static HTML without calling the app", async t => {
    const upstream = t.mock.method(globalThis, "fetch", () => { throw new Error("unexpected origin request"); });
    const page = await worker.fetch(new Request(`${publicOrigin}/?test=1`, {
        headers: { Cookie: "JobTracker.Antiforgery=anonymous; theme=dark" },
    }), {
        PUBLIC_ORIGIN: publicOrigin,
        ASSETS: {
            fetch(request) {
                assert.equal(new URL(request.url).pathname, "/homepage.html");
                assert.equal(request.headers.has("Cookie"), false);
                return new Response("<h1>Static homepage</h1>", { headers: { "Content-Type": "text/html" } });
            },
        },
    });
    assert.equal(page.status, 200);
    assert.equal(page.headers.get("Cache-Control"), "private, no-store");
    assert.equal(page.headers.get("Vary"), "Cookie, Authorization");
    assert.equal(page.headers.has("Set-Cookie"), false);
    assert.match(await page.text(), /Static homepage/);
    assert.equal(upstream.mock.callCount(), 0);
});

test("account requests go to the app with their original cookies, method, and redirect", async t => {
    const inputs = [
        new Request(publicOrigin, { headers: { Cookie: "JobTracker.Auth=existing" } }),
        new Request(publicOrigin, { headers: { Cookie: "JobTracker.AuthC1=existing-chunk" } }),
        new Request(publicOrigin, { headers: { Cookie: ".AspNetCore.Mvc.CookieTempDataProvider=flash" } }),
        new Request(publicOrigin, { headers: { Authorization: "Bearer example" } }),
        new Request(`${publicOrigin}/account/login`, { method: "POST", body: "synthetic" }),
        new Request(`${publicOrigin}/dashboard`),
    ];
    const upstream = t.mock.method(globalThis, "fetch", (request, init) => {
        assert.equal(init.cache, "no-store");
        assert.equal(init.redirect, "manual");
        assert.equal(request, inputs[upstream.mock.callCount()]);
        return Response.redirect(`${publicOrigin}/dashboard`, 302);
    });
    for (const request of inputs) {
        const reply = await worker.fetch(request, { PUBLIC_ORIGIN: publicOrigin });
        assert.equal(reply.status, 302);
        assert.equal(reply.headers.get("Location"), `${publicOrigin}/dashboard`);
    }
    assert.equal(upstream.mock.callCount(), inputs.length);
});

test("preview forwards sign-in navigation and keeps the internal HTML asset private", async () => {
    const env = { PUBLIC_ORIGIN: publicOrigin };
    const login = await worker.fetch(new Request(`${previewOrigin}/account/login?source=preview`), env);
    assert.equal(login.headers.get("Location"), `${publicOrigin}/account/login?source=preview`);
    const internal = await worker.fetch(new Request(`${previewOrigin}/homepage.html`), env);
    assert.equal(internal.status, 404);
});

test("video supports byte ranges without reaching the app", async () => {
    const env = {
        PUBLIC_ORIGIN: publicOrigin,
        ASSETS: {
            fetch(request) {
                assert.equal(request.headers.has("Range"), false);
                return new Response("12345678", { headers: assetHeaders });
            },
        },
    };
    const url = `${previewOrigin}/landing-video/0123456789abcdef/extension-demo.webm`;
    for (const [range, status, expected, body] of [
        ["bytes=0-1", 206, "bytes 0-1/8", "12"],
        ["bytes=-2", 206, "bytes 6-7/8", "78"],
        ["bytes=0-", 206, "bytes 0-7/8", "12345678"],
        ["bytes=8-", 416, "bytes */8", ""],
    ]) {
        const reply = await worker.fetch(new Request(url, { headers: { Range: range } }), env);
        assert.equal(reply.status, status);
        assert.equal(reply.headers.get("Accept-Ranges"), "bytes");
        assert.equal(reply.headers.get("Content-Range"), expected);
        assert.equal(await reply.text(), body);
    }
});
