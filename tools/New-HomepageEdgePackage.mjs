import { spawn } from "node:child_process";
import { createHash } from "node:crypto";
import { mkdir, readFile, writeFile } from "node:fs/promises";
import { dirname, join, resolve } from "node:path";
import { fileURLToPath } from "node:url";

const files = [
    "css/site.css",
    "favicon.svg",
    "js/theme.js",
    "js/navigation.js",
    "js/cookie-consent.js",
    "js/landing-video.js",
    "images/landing/dashboard-demo.png",
    "images/landing/dashboard-demo.webp",
    "images/landing/dashboard-demo-mobile.webp",
    "images/landing/extension-demo-poster.webp",
    "images/landing/extension-demo.webm",
    "images/landing/extension-demo.mp4",
];
const output = fileURLToPath(new URL("../artifacts/homepage-edge/", import.meta.url));
const matchesAsset = new RegExp(`/(${files.map(name => name.replaceAll(".", "\\.")).join("|")})(?:\\?v=[\\w-]+)?(?=["'\\s,])`, "g");
const isVideo = name => /\.(webm|mp4)$/.test(name);

async function exportHomepage(published) {
    const app = spawn("dotnet", ["JobTracker.Web.dll"], {
        cwd: published,
        env: {
            ...process.env,
            ASPNETCORE_ENVIRONMENT: "Production",
            DOTNET_ENVIRONMENT: "Production",
            ASPNETCORE_URLS: "http://127.0.0.1:0",
            AllowedHosts: "127.0.0.1",
            CanonicalOrigin: "",
            ConnectionStrings__DefaultConnection: "Host=127.0.0.1;Port=1;Database=homepage_export;Username=unused;Timeout=1",
            Email__Endpoint: "https://email.example.test",
            Email__SenderAddress: "sender@example.test",
            Email__SupportAddress: "support@example.test",
            Email__PublicBaseUrl: "https://myjobtracker.com.au",
            Logging__LogLevel__Microsoft_Hosting_Lifetime: "Information",
        },
        windowsHide: true,
    });
    const closed = new Promise(resolve => app.once("close", resolve));
    try {
        const origin = await new Promise((resolve, reject) => {
            const timer = setTimeout(() => reject(new Error("The homepage renderer did not start.")), 15000);
            let output = "";
            app.once("error", reject);
            app.once("exit", code => {
                clearTimeout(timer);
                reject(new Error(`The homepage renderer exited with code ${code}.`));
            });
            app.stderr.resume();
            app.stdout.on("data", chunk => {
                output = (output + chunk).slice(-4096);
                const address = output.match(/Now listening on: (http:\/\/127\.0\.0\.1:\d+)/)?.[1];
                if (address) {
                    clearTimeout(timer);
                    resolve(address);
                }
            });
        });
        const response = await fetch(origin, { redirect: "manual", signal: AbortSignal.timeout(10000) });
        const html = await response.text();
        if (!response.ok) throw new Error(`Homepage renderer returned HTTP ${response.status}.`);
        for (const cookie of response.headers.getSetCookie()) {
            if (!/^\.AspNetCore\.Mvc\.CookieTempDataProvider=;.*\bexpires=Thu, 01 Jan 1970/i.test(cookie)) {
                throw new Error(`Homepage renderer unexpectedly set a cookie named ${cookie.split("=", 1)[0]}.`);
            }
        }
        if (!html.includes("data-landing-dashboard") || !html.includes('href="/account/login"')) {
            throw new Error("The rendered page is missing the dashboard preview or sign-in link.");
        }
        if (/<form\b|__RequestVerificationToken|href="\/dashboard"/i.test(html)) {
            throw new Error("A private form or dashboard link appeared in the homepage export.");
        }
        return html;
    } finally {
        app.kill();
        await closed;
    }
}

async function build(published) {
    const sources = new Map(await Promise.all(files.map(async name => [name, await readFile(join(published, "wwwroot", name))])));
    const fingerprint = createHash("sha256");
    for (const [name, content] of sources) {
        if (isVideo(name) && content.length > 8 * 1024 * 1024) {
            throw new Error(`${name} exceeds the homepage video's 8 MiB range limit.`);
        }
        fingerprint.update(name).update(content);
    }
    const version = fingerprint.digest("hex").slice(0, 16);
    const assetPath = name => isVideo(name)
        ? `/landing-video/${version}/${name.split("/").at(-1)}`
        : `/landing-assets/${version}/${name}`;
    const found = new Set();
    const html = (await exportHomepage(published)).replace(matchesAsset, (_, name) => {
        found.add(name);
        return assetPath(name);
    });
    if (found.size !== sources.size) {
        throw new Error("The rendered homepage's asset references changed. Update the homepage export.");
    }

    await Promise.all([...sources].map(async ([name, content]) => {
        const destination = join(output, assetPath(name).slice(1));
        await mkdir(dirname(destination), { recursive: true });
        await writeFile(destination, content);
    }));
    const publicHeaders = ["/landing-assets/*", "/landing-video/*"].map(path =>
        `${path}\n  Cache-Control: public, max-age=31536000, immutable\n  X-Content-Type-Options: nosniff\n`);
    for (const [name, content] of sources) {
        if (isVideo(name)) {
            publicHeaders.push(`${assetPath(name)}\n  X-Homepage-Video-Length: ${content.length}\n`);
        }
    }
    await writeFile(join(output, "_headers"), publicHeaders.join("\n"));
    await writeFile(join(output, "homepage.html"), html);
    console.log(`Built signed-out homepage and assets in ${output}`);
}

if (process.argv.length !== 3) {
    console.error("Usage: node tools/New-HomepageEdgePackage.mjs <published app directory>");
    process.exitCode = 1;
} else {
    try {
        await build(resolve(process.argv[2]));
    } catch (error) {
        console.error(error.message);
        process.exitCode = 1;
    }
}
