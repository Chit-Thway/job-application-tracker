const homepageAsset = "/homepage.html";
const videoAsset = /^\/landing-video\/[a-f0-9]{16}\/extension-demo\.(webm|mp4)$/;
const appCookies = /(?:^|;\s*)(?:JobTracker\.Auth|\.AspNetCore\.Mvc\.CookieTempDataProvider)(?:C\d+)?=/;
const maxVideoBytes = 8 * 1024 * 1024;

export default {
    async fetch(request, env) {
        const url = new URL(request.url);
        const read = request.method === "GET" || request.method === "HEAD";

        if (url.pathname === homepageAsset) {
            return new Response("Not found", { status: 404 });
        }

        if (read && videoAsset.test(url.pathname)) {
            return serveVideo(request, env.ASSETS);
        }

        if (read && url.pathname === "/" && !request.headers.has("Authorization")
            && !appCookies.test(request.headers.get("Cookie") ?? "")) {
            const assetUrl = new URL(homepageAsset, url);
            const page = await env.ASSETS.fetch(new Request(assetUrl, { method: request.method }));
            if (page.ok) {
                const headers = new Headers(page.headers);
                headers.set("Cache-Control", "private, no-store");
                headers.set("Vary", "Cookie, Authorization");
                headers.append("Server-Timing", 'homepage;desc="Cloudflare"');
                headers.set("X-Content-Type-Options", "nosniff");
                headers.set("X-Frame-Options", "DENY");
                headers.set("Referrer-Policy", "no-referrer");
                headers.set("Content-Security-Policy", "default-src 'self'; img-src 'self' data:; style-src 'self'; script-src 'self'; font-src 'self'; object-src 'none'; base-uri 'self'; frame-ancestors 'none'; form-action 'self'");
                headers.set("Permissions-Policy", "camera=(), microphone=(), geolocation=(), payment=(), usb=()");
                headers.set("Cross-Origin-Opener-Policy", "same-origin");
                headers.set("Cross-Origin-Resource-Policy", "same-origin");
                if (url.origin === env.PUBLIC_ORIGIN) {
                    headers.set("Strict-Transport-Security", "max-age=2592000");
                } else {
                    headers.set("X-Robots-Tag", "noindex");
                }
                return new Response(page.body, { status: page.status, headers });
            }
        }

        // A Worker Route can reach the configured Azure origin at the original hostname.
        // The standalone preview instead sends the browser to the real site.
        if (url.origin === env.PUBLIC_ORIGIN) {
            return fetch(request, { cache: "no-store", redirect: "manual" });
        }
        const destination = new URL(env.PUBLIC_ORIGIN);
        destination.pathname = url.pathname;
        destination.search = url.search;
        return read
            ? Response.redirect(destination, 302)
            : new Response("Open the tracker to continue.", { status: 405, headers: { Allow: "GET, HEAD" } });
    },
};

async function serveVideo(request, assets) {
    const range = request.headers.get("Range")?.match(/^bytes=(\d*)-(\d*)$/i);
    const assetRequest = new Request(request);
    assetRequest.headers.delete("Range");
    assetRequest.headers.set("Accept-Encoding", "identity");
    const asset = await assets.fetch(assetRequest);
    if (asset.status !== 200) return asset;

    const headers = new Headers(asset.headers);
    headers.set("Accept-Ranges", "bytes");
    const size = Number(headers.get("X-Homepage-Video-Length") ?? headers.get("Content-Length"));
    headers.delete("X-Homepage-Video-Length");
    const ifRange = request.headers.get("If-Range");
    if (request.method === "HEAD" || !range || (ifRange && ifRange !== headers.get("ETag")
        && ifRange !== headers.get("Last-Modified")) || headers.has("Content-Encoding")
        || !Number.isSafeInteger(size) || size <= 0 || size > maxVideoBytes) {
        return new Response(asset.body, { headers });
    }

    const [, from, to] = range;
    const start = from ? Number(from) : Math.max(0, size - Number(to));
    const end = from && to ? Math.min(Number(to), size - 1) : size - 1;
    if (start >= size || end < start) {
        await asset.body?.cancel();
        headers.set("Content-Range", `bytes */${size}`);
        headers.set("Content-Length", "0");
        return new Response(null, { status: 416, headers });
    }

    headers.set("Content-Range", `bytes ${start}-${end}/${size}`);
    headers.set("Content-Length", String(end - start + 1));
    if (start === 0 && end === size - 1) {
        return new Response(asset.body, { status: 206, headers });
    }
    const data = await asset.arrayBuffer();
    return new Response(data.slice(start, end + 1), { status: 206, headers });
}
