(() => {
    const isSupported = () =>
        "serviceWorker" in navigator &&
        "PushManager" in window &&
        "Notification" in window;

    const antiForgeryToken = () =>
        document.querySelector('input[name="__RequestVerificationToken"]')?.value || "";
    const browserPushCard = document.getElementById("browserNotificationCard");
    const nativePushCard = document.getElementById("nativeFcmNotificationCard");
    const nativePushStatus = document.querySelector("[data-fcm-notification-status]");
    const nativeEnableButton = document.querySelector("[data-fcm-enable-button]");
    const nativeDisableButton = document.querySelector("[data-fcm-disable-button]");
    let nativeFcmInitialized = false;

    const getFcmBridge = () => window.median?.firebaseMessaging || null;

    const invokeFcm = (method, options = {}) => new Promise((resolve, reject) => {
        const bridge = getFcmBridge();
        if (!bridge || typeof bridge[method] !== "function") {
            reject(new Error("Native Firebase notifications are not available in this app build."));
            return;
        }

        let completed = false;
        const timeout = window.setTimeout(() => {
            if (completed) return;
            completed = true;
            reject(new Error("The notification service did not respond. Please try again."));
        }, 15000);

        try {
            bridge[method]({
                ...options,
                callback: result => {
                    if (completed) return;
                    completed = true;
                    window.clearTimeout(timeout);
                    resolve(result || {});
                }
            });
        } catch (error) {
            if (completed) return;
            completed = true;
            window.clearTimeout(timeout);
            reject(error);
        }
    });

    const getNativePlatform = () =>
        /iPad|iPhone|iPod/i.test(navigator.userAgent) ? "ios" : "android";

    const setNativeEnabledUi = () => {
        if (nativePushStatus) {
            nativePushStatus.textContent = "Native notifications are enabled on this device. PharmaHex can now deliver stock and savings alerts even when the app is closed.";
        }
        if (nativeEnableButton) nativeEnableButton.hidden = true;
        if (nativeDisableButton) {
            nativeDisableButton.hidden = false;
            nativeDisableButton.disabled = false;
        }
    };

    const setNativeDisabledUi = (message = "Notifications are off on this device. Enable them to receive PharmaHex alerts when the app is closed.") => {
        if (nativePushStatus) nativePushStatus.textContent = message;
        if (nativeEnableButton) {
            nativeEnableButton.hidden = false;
            nativeEnableButton.disabled = false;
            nativeEnableButton.textContent = "Enable";
        }
        if (nativeDisableButton) nativeDisableButton.hidden = true;
    };

    const saveFcmDeviceToken = async token => {
        if (!token || typeof token !== "string")
            throw new Error("Firebase did not return a device token.");

        await postJson("/Notifications/RegisterFcmDevice", {
            token,
            platform: getNativePlatform(),
            permissionGranted: true
        });
    };

    const createNativeNotificationChannel = () => {
        const bridge = getFcmBridge();
        if (!bridge || typeof bridge.createChannel !== "function") return;

        invokeFcm("createChannel", {
            channelId: "pharmahex_alerts",
            channelName: "PharmaHex alerts",
            importance: "high"
        }).catch(() => {});
    };

    const refreshNativeFcmRegistration = async () => {
        const bridge = getFcmBridge();
        if (!bridge) return false;

        nativePushCard && (nativePushCard.hidden = false);
        if (browserPushCard) browserPushCard.hidden = true;

        // Do not silently opt a user in on Android versions where the OS
        // grants notification permission without displaying a prompt.
        if (localStorage.getItem("pharmahex-native-push-opt-in") !== "true") {
            setNativeDisabledUi();
            return false;
        }

        try {
            const permission = await invokeFcm("checkPermission");
            if (!permission.granted) {
                setNativeDisabledUi();
                return false;
            }

            createNativeNotificationChannel();

            let result = await invokeFcm("getToken");
            if (!result.token) {
                result = await invokeFcm("register");
            }

            if (!result.token)
                throw new Error("Firebase has not generated a device token yet.");

            await saveFcmDeviceToken(result.token);
            setNativeEnabledUi();
            return true;
        } catch (error) {
            console.warn("PharmaHex native notification registration could not be synced.", error);
            setNativeDisabledUi("Notifications are allowed, but this device could not be registered with PharmaHex. Reopen the app or try enabling notifications again.");
            return false;
        }
    };

    const enableNativeFcm = async () => {
        if (!getFcmBridge()) {
            alert("Native Firebase notifications are not available in this app build. Rebuild the Median app with Firebase Cloud Messaging enabled.");
            return false;
        }

        if (nativeEnableButton) nativeEnableButton.disabled = true;
        if (nativePushStatus) nativePushStatus.textContent = "Requesting notification permission...";

        try {
            const permission = await invokeFcm("requestPermission");
            if (!permission.granted) {
                setNativeDisabledUi("Notification permission was not granted. Allow notifications in Android settings to receive PharmaHex alerts.");
                return false;
            }

            createNativeNotificationChannel();

            let result = await invokeFcm("register");
            if (!result.token) result = await invokeFcm("getToken");
            if (!result.token)
                throw new Error("Firebase registration succeeded, but no device token was returned.");

            await saveFcmDeviceToken(result.token);
            localStorage.setItem("pharmahex-native-push-opt-in", "true");
            setNativeEnabledUi();

            // A test push is best-effort. Device registration remains enabled
            // even while the server-side Firebase credential is being configured.
            try {
                await postJson("/Notifications/Test", {});
            } catch (error) {
                console.info("Native device registered; test push is not available yet.", error);
            }

            return true;
        } catch (error) {
            console.error("PharmaHex native notification setup failed.", error);
            setNativeDisabledUi(error?.message || "Native notifications could not be enabled. Please try again.");
            return false;
        } finally {
            if (nativeEnableButton && !nativeEnableButton.hidden)
                nativeEnableButton.disabled = false;
        }
    };

    const disableNativeFcm = async () => {
        if (nativeDisableButton) nativeDisableButton.disabled = true;

        try {
            const result = await invokeFcm("getToken").catch(() => ({}));
            if (result.token) {
                await postJson("/Notifications/UnregisterFcmDevice", { token: result.token });
            }

            await invokeFcm("deleteToken");
            localStorage.removeItem("pharmahex-native-push-opt-in");
            setNativeDisabledUi("Native notifications are turned off on this device.");
            return true;
        } catch (error) {
            console.error("PharmaHex native notification disable failed.", error);
            if (nativePushStatus)
                nativePushStatus.textContent = error?.message || "Notifications could not be turned off. Please try again.";
            return false;
        } finally {
            if (nativeDisableButton) nativeDisableButton.disabled = false;
        }
    };

    const handleNativeFcmLogout = () => {
        document.querySelectorAll('form[action*="/Account/Logout"], form[action*="Account/Logout"]').forEach(form => {
            form.addEventListener("submit", async event => {
                if (!getFcmBridge() || form.dataset.nativeFcmLogoutStarted === "true")
                    return;

                event.preventDefault();
                form.dataset.nativeFcmLogoutStarted = "true";

                try {
                    const result = await invokeFcm("getToken").catch(() => ({}));
                    if (result.token) {
                        await postJson("/Notifications/UnregisterFcmDevice", { token: result.token });
                    }
                    await invokeFcm("deleteToken").catch(() => {});
                    localStorage.removeItem("pharmahex-native-push-opt-in");
                } catch (error) {
                    console.warn("Could not unlink this native notification device before logout.", error);
                } finally {
                    form.submit();
                }
            });
        });
    };

    const activateNativeFcmUi = () => {
        if (nativeFcmInitialized || !getFcmBridge()) return;
        nativeFcmInitialized = true;

        if (nativePushCard) nativePushCard.hidden = false;
        if (browserPushCard) browserPushCard.hidden = true;

        nativeEnableButton?.addEventListener("click", enableNativeFcm);
        nativeDisableButton?.addEventListener("click", disableNativeFcm);
        handleNativeFcmLogout();
        refreshNativeFcmRegistration();
    };

    // Median injects the bridge asynchronously. This callback is the supported
    // page-load hook; the fallback also covers cases where the bridge is ready first.
    window.median_library_ready = activateNativeFcmUi;
    if (window.median) activateNativeFcmUi();


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
        if (getFcmBridge()) activateNativeFcmUi();

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
