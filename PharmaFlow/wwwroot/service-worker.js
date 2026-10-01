const CACHE_NAME = "pharmaflow-offline-v2";
const OFFLINE_URL = "/offline.html";

self.addEventListener("install", event => {
    event.waitUntil(
        caches.open(CACHE_NAME)
            .then(cache => cache.add(OFFLINE_URL))
            .then(() => self.skipWaiting())
    );
});

self.addEventListener("activate", event => {
    event.waitUntil(
        caches.keys()
            .then(keys => Promise.all(
                keys
                    .filter(key => key !== CACHE_NAME)
                    .map(key => caches.delete(key))
            ))
            .then(() => self.clients.claim())
    );
});

self.addEventListener("fetch", event => {
    if (event.request.mode !== "navigate") return;

    event.respondWith(
        fetch(event.request).catch(() => caches.match(OFFLINE_URL))
    );
});

self.addEventListener("push", event => {
    let payload = {};

    try {
        payload = event.data ? event.data.json() : {};
    } catch {
        payload = {
            title: "PharmaFlow",
            body: event.data?.text() || "You have a new PharmaFlow update."
        };
    }

    const title = payload.title || "PharmaFlow";
    const options = {
        body: payload.body || "You have a new PharmaFlow update.",
        icon: "/favicon.ico",
        badge: "/favicon.ico",
        tag: payload.tag || "pharmaflow-notification",
        renotify: Boolean(payload.renotify),
        data: {
            url: payload.url || "/Home"
        }
    };

    event.waitUntil(
        self.registration.showNotification(title, options)
    );
});

self.addEventListener("notificationclick", event => {
    event.notification.close();

    const targetUrl = event.notification?.data?.url || "/Home";

    event.waitUntil(
        clients.matchAll({ type: "window", includeUncontrolled: true })
            .then(clientList => {
                const existing = clientList.find(client => "focus" in client);
                if (existing) {
                    existing.navigate(targetUrl);
                    return existing.focus();
                }

                if (clients.openWindow)
                    return clients.openWindow(targetUrl);

                return undefined;
            })
    );
});
