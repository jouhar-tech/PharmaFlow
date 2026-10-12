// Please see documentation at https://learn.microsoft.com/aspnet/core/client-side/bundling-and-minification
// for details on configuring this project to bundle and minify static web assets.

// Write your JavaScript code.


/* Shared navigation progress indicator and skeleton screen for full-page navigation and form posts. */
(() => {
    const progress = document.getElementById("globalPageProgress");
    const skeleton = document.getElementById("pageSkeleton");
    const pageContent = document.getElementById("pageContent");
    if (!progress) return;

    let hideTimer = 0;
    let pendingTimer = 0;
    const initialSkeletonType = skeleton?.dataset.pageType || "generic";

    const inferSkeletonType = (url) => {
        if (!url) return initialSkeletonType;

        let path;
        try {
            path = new URL(url, window.location.href).pathname.toLowerCase();
        } catch {
            return "generic";
        }

        const segments = path.split("/").filter(Boolean);
        const controller = segments[0] || "home";
        const action = segments[1] || "index";

        if (controller === "home" && action === "index") return "dashboard";
        if (["details", "detail", "generated", "generate"].includes(action)) return "detail";
        if (["create", "add", "additem", "payment", "createreminder", "review", "save", "edit", "feedback", "unlimitedplan", "barcodescanner"].includes(action)) return "form";
        if (["index", "searchproducts", "distributor", "slowmoving", "lowstock", "reorder"].includes(action)) return "list";
        return "generic";
    };

    const show = (destinationUrl) => {
        window.clearTimeout(hideTimer);
        progress.classList.add("is-active");
        progress.setAttribute("aria-hidden", "false");

        if (skeleton) skeleton.dataset.pageType = inferSkeletonType(destinationUrl);
        document.documentElement.classList.add("page-skeleton-visible");
        pageContent?.setAttribute("aria-busy", "true");

        // Start the new view at its natural top edge so the skeleton is not
        // displayed halfway down the previous page after a long scroll.
        window.scrollTo(0, 0);
    };

    const hide = () => {
        window.clearTimeout(hideTimer);
        window.clearTimeout(pendingTimer);
        progress.classList.remove("is-active");
        progress.setAttribute("aria-hidden", "true");
        document.documentElement.classList.remove("page-skeleton-visible");
        pageContent?.setAttribute("aria-busy", "false");
        if (skeleton) skeleton.dataset.pageType = initialSkeletonType;
    };

    const showIfNavigationContinues = (event, destinationUrl) => {
        window.clearTimeout(pendingTimer);
        pendingTimer = window.setTimeout(() => {
            if (event.defaultPrevented) return;
            show(destinationUrl);
            // Keep the indicator from getting stuck on scripted/AJAX submissions
            // or navigation attempts that the browser ultimately does not complete.
            hideTimer = window.setTimeout(hide, 15000);
        }, 80);
    };

    document.addEventListener("click", (event) => {
        if (event.defaultPrevented || event.button !== 0 ||
            event.metaKey || event.ctrlKey || event.shiftKey || event.altKey) return;

        const target = event.target instanceof Element ? event.target : null;
        const link = target?.closest("a[href]");
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

        showIfNavigationContinues(event, destination.href);
    }, true);

    document.addEventListener("submit", (event) => {
        const form = event.target;
        if (!(form instanceof HTMLFormElement) ||
            event.defaultPrevented ||
            form.hasAttribute("data-no-page-progress") ||
            (form.target && form.target.toLowerCase() !== "_self")) return;

        const method = (form.method || "get").toLowerCase();
        if (method === "dialog") return;

        let destinationUrl = form.action || window.location.href;
        try {
            destinationUrl = new URL(destinationUrl, window.location.href).href;
        } catch {
            destinationUrl = window.location.href;
        }
        showIfNavigationContinues(event, destinationUrl);
    }, true);

    window.addEventListener("pageshow", hide);
    window.addEventListener("pagehide", () => {
        window.clearTimeout(hideTimer);
        window.clearTimeout(pendingTimer);
    });
    window.addEventListener("load", hide, { once: true });
})();
