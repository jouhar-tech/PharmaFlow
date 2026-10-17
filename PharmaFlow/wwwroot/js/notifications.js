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

    const setEnabledUi = () => {
        document.querySelectorAll("[data-notification-button]").forEach(element => {
            element.classList.add("is-enabled");
        });

        document.querySelectorAll("[data-notification-status]").forEach(status => {
            status.textContent = "Browser notifications are enabled on this device. Scheduled stock and savings reminders can now be delivered here.";
        });

        document.querySelectorAll("[data-notification-enable-button]").forEach(button => {
            button.hidden = true;
        });
        document.querySelectorAll("[data-notification-disable-button]").forEach(button => {
            button.hidden = false;
            button.disabled = false;
        });
    };

    const setDisabledUi = () => {
        document.querySelectorAll("[data-notification-button]").forEach(element => {
            element.classList.remove("is-enabled");
        });

        document.querySelectorAll("[data-notification-status]").forEach(status => {
            status.textContent = "Browser notifications are off on this device. You can still check live alerts on this page.";
        });

        document.querySelectorAll("[data-notification-enable-button]").forEach(button => {
            button.hidden = false;
            button.disabled = false;
            button.textContent = "Enable";
            button.setAttribute("aria-label", "Enable browser notifications");
        });
        document.querySelectorAll("[data-notification-disable-button]").forEach(button => {
            button.hidden = true;
            button.disabled = false;
        });
    };

    const ensureExistingSubscriptionSynced = async () => {
        if (!isSupported() || Notification.permission !== "granted") return false;

        const syncKey = "pharmaflow-notification-sync";
        const syncIntervalMs = 24 * 60 * 60 * 1000;
        const lastSync = Number(localStorage.getItem(syncKey) || "0");

        if (Number.isFinite(lastSync) && Date.now() - lastSync < syncIntervalMs)
            return false;

        try {
            const registration = await navigator.serviceWorker.ready;
            const subscription = await registration.pushManager.getSubscription();

            if (!subscription) return false;

            await saveCurrentSubscription(subscription);
            localStorage.setItem(syncKey, String(Date.now()));
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
            let createdNewSubscription = false;

            if (!subscription) {
                subscription = await registration.pushManager.subscribe({
                    userVisibleOnly: true,
                    applicationServerKey: base64UrlToBytes(publicKey)
                });
                createdNewSubscription = true;
            }

            await saveCurrentSubscription(subscription);
            localStorage.setItem("pharmaflow-notification-sync", String(Date.now()));

            setEnabledUi();

            if (createdNewSubscription) {
                try {
                    await postJson("/Notifications/Test", {});
                } catch {
                    // Registration succeeded even if the immediate test delivery fails.
                }
            }

            return true;
        } catch (error) {
            console.error("PharmaFlow notification setup failed.", error);
            alert(error?.message || "Notifications could not be enabled. Please try again.");
            return false;
        }
    };

    const disable = async () => {
        if (!isSupported()) {
            setDisabledUi();
            return true;
        }

        try {
            const registration = await navigator.serviceWorker.ready;
            const subscription = await registration.pushManager.getSubscription();

            if (subscription) {
                const json = subscription.toJSON();
                if (!json.endpoint)
                    throw new Error("The current browser subscription has no endpoint.");

                // Deactivate this device on the server before removing the local browser subscription.
                await postJson("/Notifications/Unsubscribe", { endpoint: json.endpoint });
                await subscription.unsubscribe();
            }

            localStorage.removeItem("pharmaflow-notification-sync");
            setDisabledUi();
            return true;
        } catch (error) {
            console.error("PharmaFlow notification disable failed.", error);
            alert(error?.message || "Notifications could not be turned off. Please try again.");
            return false;
        }
    };

    const markEnabledButtons = () => {
        if (!isSupported() || Notification.permission !== "granted") return;

        navigator.serviceWorker.ready
            .then(registration => registration.pushManager.getSubscription())
            .then(subscription => {
                if (!subscription) return;
                setEnabledUi();
            })
            .catch(() => {});
    };

    const init = () => {
        const enableButtons = document.querySelectorAll("[data-notification-enable-button]");
        const disableButtons = document.querySelectorAll("[data-notification-disable-button]");

        if (!isSupported()) {
            document.querySelectorAll("[data-notification-status]").forEach(status => {
                status.textContent = "This browser does not support web push notifications. Your live alerts are still available on this page.";
            });
            enableButtons.forEach(button => {
                button.disabled = true;
                button.textContent = "Not supported";
            });
            disableButtons.forEach(button => {
                button.hidden = true;
            });
            return;
        }

        enableButtons.forEach(button => {
            button.addEventListener("click", () => enable());
        });
        disableButtons.forEach(button => {
            button.addEventListener("click", () => disable());
        });

        ensureExistingSubscriptionSynced();
        markEnabledButtons();
    };

    window.pharmaFlowNotifications = { enable, disable, isSupported };

    document.addEventListener("DOMContentLoaded", init);
})();
