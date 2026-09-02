(() => {
    const container = document.querySelector("[data-extension-video]");
    const video = container?.querySelector("[data-extension-video-player]");
    if (!container || !video) {
        return;
    }

    const startPlayback = () => {
        if (container.classList.contains("has-started")) {
            return;
        }

        video.play().then(() => {
            container.classList.add("has-started");
            container.removeAttribute("role");
            container.removeAttribute("tabindex");
            container.removeAttribute("aria-label");
        }).catch(() => {
            // The initial cover remains available if playback cannot start.
        });
    };

    container.addEventListener("click", startPlayback);
    container.addEventListener("keydown", event => {
        if (event.key !== "Enter" && event.key !== " ") {
            return;
        }

        event.preventDefault();
        startPlayback();
    });
})();
