(() => {
    const container = document.querySelector("[data-extension-video]");
    const video = container?.querySelector("[data-extension-video-player]");
    const sources = container?.querySelector("template");
    const play = container?.querySelector("button");
    const status = container?.querySelector("[data-extension-video-error]");
    const dashboard = document.querySelector("[data-landing-dashboard]");
    if (!video || !sources || !play || !status || !dashboard) {
        return;
    }
    let sourcesFailed = false;
    let playbackAttempt = 0;

    function prepareVideo() {
        if (!video.firstElementChild) {
            video.preload = "auto";
            video.append(sources.content.cloneNode(true));
            video.load();
        }
    }

    function reportPlaybackError() {
        status.textContent = "The video couldn’t play. Try again.";
        play.removeAttribute("aria-busy");
    }

    video.addEventListener("error", event => {
        // The browser tries each source in order. Only report failure after the last one.
        if (event.target === video || event.target === video.lastElementChild) {
            sourcesFailed = true;
            if (play.hasAttribute("aria-busy")) {
                reportPlaybackError();
            }
        }
    }, true);

    play.addEventListener("click", async () => {
        if (play.hasAttribute("aria-busy")) {
            return;
        }
        const attempt = ++playbackAttempt;
        if (sourcesFailed) {
            video.replaceChildren();
            sourcesFailed = false;
        }
        status.textContent = "";
        play.setAttribute("aria-busy", "true");
        prepareVideo();

        try {
            await video.play();
            video.controls = true;
            container.classList.add("has-started");
            if (document.activeElement === play) {
                video.focus();
            }
            play.hidden = true;
            play.removeAttribute("aria-busy");
        } catch {
            if (attempt === playbackAttempt) {
                reportPlaybackError();
            }
        }
    });
    play.hidden = false;

    async function waitForDashboardPaint() {
        await new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve)));
        if (!window.PerformanceObserver?.supportedEntryTypes?.includes("element")) {
            await new Promise(resolve => setTimeout(resolve, 150));
            return;
        }

        await new Promise(resolve => {
            const finish = () => {
                observer.disconnect();
                clearTimeout(fallback);
                resolve();
            };
            const observer = new PerformanceObserver(list => {
                if (list.getEntries().some(entry => entry.element === dashboard && entry.renderTime > 0)) {
                    finish();
                }
            });
            // An image below the fold, or one that fails to load, may have no paint entry.
            const fallback = setTimeout(finish, 750);
            observer.observe({ type: "element", buffered: true });
        });
    }

    async function bufferAfterPaint() {
        // A failed image should not prevent buffering once the rest of the page is visible.
        await dashboard.decode().catch(() => {});
        await waitForDashboardPaint();
        const buffer = () => {
            if (!navigator.connection?.saveData) {
                prepareVideo();
            }
        };
        if ("requestIdleCallback" in window) {
            requestIdleCallback(buffer);
        } else {
            setTimeout(buffer, 0);
        }
    }

    if (!navigator.connection?.saveData) {
        if (document.readyState === "complete") {
            void bufferAfterPaint();
        } else {
            window.addEventListener("load", bufferAfterPaint, { once: true });
        }
    }
})();
