(() => {
    const isSupported = () =>
        "serviceWorker" in navigator &&
        "PushManager" in window &&
        "Notification" in window;

    const antiForgeryToken = () =>
        document.querySelector('input[name="__RequestVerificationToken"]')?.value || "";

    const base64UrlToBytes = value => {
        const normalized = value.replace(/-/g, "+").replace(/_/g, "/");
        const padded = normalized.padEnd(Math.ceil(normalized.length / 4) * 4, "=");
        const raw = window.atob(padded);
        return Uint8Array.from(raw, char => char.charCodeAt(0));
    };

    const postJson = async (url, payload) => {
        const token = antiForgeryToken();
        if (!token) throw new Error("Notification security token is missing.");

        const response = await fetch(url, {
            method: "POST",
            credentials: "same-origin",
            headers: {
                "Content-Type": "application/json",
                "RequestVerificationToken": token,
                "X-Requested-With": "XMLHttpRequest"
            },
            body: JSON.stringify(payload)
        });

        if (!response.ok)
            throw new Error("Notification request failed (" + response.status + ").");

        return response;
    };

    const saveCurrentSubscription = async subscription => {
        const json = subscription.toJSON();

        if (!json.endpoint || !json.keys?.p256dh || !json.keys?.auth)
            throw new Error("The browser returned an incomplete notification subscription.");

        await postJson("/Notifications/Subscribe", {
            endpoint: json.endpoint,
            p256dh: json.keys.p256dh,
            auth: json.keys.auth
        });
    };

    const ensureExistingSubscriptionSynced = async () => {
        if (!isSupported() || Notification.permission !== "granted") return false;

        try {
            const registration = await navigator.serviceWorker.ready;
            const subscription = await registration.pushManager.getSubscription();

            if (!subscription) return false;

            await saveCurrentSubscription(subscription);
            return true;
        } catch {
            return false;
        }
    };

    const enable = async () => {
        if (!isSupported()) {
            alert("Push notifications are not supported by this browser.");
            return false;
        }

        try {
            const permission = await Notification.requestPermission();

            if (permission !== "granted") {
                alert(
                    permission === "denied"
                        ? "Notifications are blocked. Please allow notifications in your browser settings."
                        : "Notifications were not enabled."
                );
                return false;
            }

            const publicKeyResponse = await fetch("/Notifications/PublicKey", {
                credentials: "same-origin",
                cache: "no-store"
            });

            if (!publicKeyResponse.ok)
                throw new Error("Push notification configuration is not available yet.");

            const { publicKey } = await publicKeyResponse.json();
            const registration = await navigator.serviceWorker.ready;

            let subscription = await registration.pushManager.getSubscription();

            if (!subscription) {
                subscription = await registration.pushManager.subscribe({
                    userVisibleOnly: true,
                    applicationServerKey: base64UrlToBytes(publicKey)
                });
            }

            await saveCurrentSubscription(subscription);

            document.querySelectorAll("[data-notification-button]").forEach(button => {
                button.classList.add("is-enabled");
                button.setAttribute("aria-label", "Notifications enabled");
            });

            try {
                await postJson("/Notifications/Test", {});
            } catch {
                // Registration succeeded even if the immediate test delivery fails.
            }

            return true;
        } catch (error) {
            console.error("PharmaFlow notification setup failed.", error);
            alert(error?.message || "Notifications could not be enabled. Please try again.");
            return false;
        }
    };

    const markEnabledButtons = () => {
        if (!isSupported() || Notification.permission !== "granted") return;

        navigator.serviceWorker.ready
            .then(registration => registration.pushManager.getSubscription())
            .then(subscription => {
                if (!subscription) return;
                document.querySelectorAll("[data-notification-button]").forEach(button => {
                    button.classList.add("is-enabled");
                    button.setAttribute("aria-label", "Notifications enabled");
                });
            })
            .catch(() => {});
    };

    const init = () => {
        if (!isSupported()) return;

        document.querySelectorAll("[data-notification-button]").forEach(button => {
            button.addEventListener("click", () => enable());
        });

        ensureExistingSubscriptionSynced();
        markEnabledButtons();
    };

    window.pharmaFlowNotifications = { enable, isSupported };

    document.addEventListener("DOMContentLoaded", init);
})();
