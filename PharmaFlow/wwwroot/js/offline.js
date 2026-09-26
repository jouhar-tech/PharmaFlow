(() => {
    let browserOnline = navigator.onLine;
    let serverReachable = false;
    let bannerTimer = null;

    const translate = (key, fallback) =>
        window.pharmaFlowLanguage?.translate?.(key) || fallback;

    const isOnline = () => browserOnline && serverReachable;

    const ensureBanner = () => {
        let banner = document.getElementById("pharmaflowConnectivityBanner");

        if (banner) return banner;

        banner = document.createElement("div");
        banner.id = "pharmaflowConnectivityBanner";
        banner.className = "pharmaflow-connectivity-banner";
        banner.setAttribute("role", "status");
        banner.setAttribute("aria-live", "polite");
        banner.hidden = true;
        document.body.appendChild(banner);

        return banner;
    };

    const showBanner = (type, titleKey, messageKey, fallbackTitle, fallbackMessage, duration = null) => {
        const banner = ensureBanner();

        banner.className = `pharmaflow-connectivity-banner is-${type}`;
        banner.innerHTML = `
            <span class="pharmaflow-connectivity-banner-title">${translate(titleKey, fallbackTitle)}</span>
            <span class="pharmaflow-connectivity-banner-message">${translate(messageKey, fallbackMessage)}</span>
        `;
        banner.hidden = false;

        if (bannerTimer) window.clearTimeout(bannerTimer);

        if (duration) {
            bannerTimer = window.setTimeout(() => {
                banner.hidden = true;
            }, duration);
        }
    };

    const hideBanner = () => {
        const banner = document.getElementById("pharmaflowConnectivityBanner");
        if (banner) banner.hidden = true;
    };

    const checkServerConnection = async () => {
        if (!navigator.onLine) {
            browserOnline = false;
            serverReachable = false;
            showBanner(
                "offline",
                "offlineTitle",
                "offlineMessage",
                "You're offline",
                "You're viewing saved data. Internet connection is required to add or update information."
            );
            return false;
        }

        browserOnline = true;

        const controller = new AbortController();
        const timeout = window.setTimeout(() => controller.abort(), 5000);

        try {
            const response = await fetch("/Home/Ping", {
                method: "GET",
                cache: "no-store",
                credentials: "include",
                headers: { "X-PharmaFlow-Connectivity": "1" },
                signal: controller.signal
            });

            serverReachable = response.status === 200;
        } catch {
            serverReachable = false;
        } finally {
            window.clearTimeout(timeout);
        }

        if (!serverReachable) {
            showBanner(
                "offline",
                "offlineTitle",
                "offlineMessage",
                "You're offline",
                "You're viewing saved data. Internet connection is required to add or update information."
            );
        } else {
            hideBanner();
        }

        return serverReachable;
    };

    const cacheDashboard = async () => {
        const dashboard = document.querySelector(".dashboard-page");
        const store = window.PharmaFlowOfflineStore;

        if (!dashboard || !store || !isOnline()) return;

        const clone = dashboard.cloneNode(true);
        clone.querySelectorAll("script,iframe,object,embed,form").forEach(element => element.remove());

        const username =
            document.querySelector(".profile-trigger-text small")?.textContent?.trim() || "current";

        const businessName =
            document.querySelector(".profile-trigger-text strong")?.textContent?.trim() || "";

        await store.put("dashboard-current", {
            html: clone.outerHTML,
            username,
            businessName,
            savedAt: new Date().toISOString(),
            language: window.pharmaFlowLanguage?.get?.() || "en"
        });
    };

    const showOnlineRequired = () => {
        showBanner(
            "offline",
            "onlineRequiredTitle",
            "onlineRequiredMessage",
            "Internet connection required",
            "This action cannot be completed while you are offline.",
            4000
        );
    };

    const attachWriteProtection = () => {
        document.addEventListener("submit", event => {
            const form = event.target.closest("form[data-online-required]");

            if (!form || isOnline()) return;

            event.preventDefault();
            showOnlineRequired();
        }, true);
    };

    const attachLogoutCleanup = () => {
        document.querySelectorAll('form[action*="Logout"]').forEach(form => {
            form.addEventListener("submit", async event => {
                if (!isOnline()) return;

                event.preventDefault();
                await window.PharmaFlowOfflineStore?.removeAll();
                form.submit();
            });
        });
    };

    const registerServiceWorker = () => {
        if (!("serviceWorker" in navigator)) return;

        navigator.serviceWorker.register("/service-worker.js").catch(() => {
            // Offline navigation is optional; the normal online application still works.
        });
    };

    const init = async () => {
        attachWriteProtection();
        attachLogoutCleanup();
        registerServiceWorker();

        await checkServerConnection();
        await cacheDashboard();

        window.addEventListener("online", async () => {
            browserOnline = true;

            const wasOffline = !serverReachable;
            const reachable = await checkServerConnection();

            if (reachable && wasOffline) {
                showBanner(
                    "online",
                    "onlineTitle",
                    "onlineMessage",
                    "You're back online",
                    "All features are available again.",
                    3500
                );
            }
        });

        window.addEventListener("offline", () => {
            browserOnline = false;
            serverReachable = false;

            showBanner(
                "offline",
                "offlineTitle",
                "offlineMessage",
                "You're offline",
                "You're viewing saved data. Internet connection is required to add or update information."
            );
        });

        window.addEventListener("pharmaflow:language-changed", () => {
            if (isOnline()) cacheDashboard();
        });

        window.setInterval(() => {
            if (!isOnline()) checkServerConnection();
        }, 30000);

        window.pharmaFlowConnectivity = {
            isOnline,
            requireOnline: showOnlineRequired
        };
    };

    document.addEventListener("DOMContentLoaded", init);
})();