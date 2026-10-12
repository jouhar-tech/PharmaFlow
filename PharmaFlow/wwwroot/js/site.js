// Please see documentation at https://learn.microsoft.com/aspnet/core/client-side/bundling-and-minification
// for details on configuring this project to bundle and minify static web assets.

// Write your JavaScript code.


/* Shared navigation progress indicator for full-page navigation and form posts. */
(() => {
    const progress = document.getElementById("globalPageProgress");
    if (!progress) return;

    let hideTimer = 0;
    let pendingTimer = 0;

    const show = () => {
        window.clearTimeout(hideTimer);
        progress.classList.add("is-active");
        progress.setAttribute("aria-hidden", "false");
    };

    const hide = () => {
        window.clearTimeout(hideTimer);
        window.clearTimeout(pendingTimer);
        progress.classList.remove("is-active");
        progress.setAttribute("aria-hidden", "true");
    };

    const showIfNavigationContinues = (event) => {
        window.clearTimeout(pendingTimer);
        pendingTimer = window.setTimeout(() => {
            if (event.defaultPrevented) return;
            show();
            // Keep the indicator from getting stuck on scripted/AJAX submissions
            // or navigation attempts that the browser ultimately does not complete.
            hideTimer = window.setTimeout(hide, 15000);
        }, 80);
    };

    document.addEventListener("click", (event) => {
        if (event.defaultPrevented || event.button !== 0 ||
            event.metaKey || event.ctrlKey || event.shiftKey || event.altKey) return;

        const link = event.target.closest("a[href]");
        if (!link || link.hasAttribute("download") ||
            link.target && link.target.toLowerCase() !== "_self" ||
            link.hasAttribute("data-no-page-progress")) return;

        const rawHref = link.getAttribute("href");
        if (!rawHref || rawHref.startsWith("#") ||
            /^(?:javascript:|mailto:|tel:|sms:)/i.test(rawHref)) return;

        let destination;
        try {
            destination = new URL(link.href, window.location.href);
        } catch {
            return;
        }

        if (destination.origin !== window.location.origin ||
            destination.href === window.location.href) return;

        showIfNavigationContinues(event);
    }, true);

    document.addEventListener("submit", (event) => {
        const form = event.target;
        if (!(form instanceof HTMLFormElement) ||
            event.defaultPrevented ||
            form.hasAttribute("data-no-page-progress") ||
            (form.target && form.target.toLowerCase() !== "_self")) return;

        const method = (form.method || "get").toLowerCase();
        if (method === "dialog") return;

        showIfNavigationContinues(event);
    }, true);

    window.addEventListener("pageshow", hide);
    window.addEventListener("pagehide", () => {
        window.clearTimeout(hideTimer);
        window.clearTimeout(pendingTimer);
    });
    window.addEventListener("load", hide, { once: true });
})();
