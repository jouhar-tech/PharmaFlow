(() => {
    const DB_NAME = "PharmaFlowOffline";
    const STORE_NAME = "snapshots";
    const DB_VERSION = 1;

    const openDatabase = () => new Promise((resolve, reject) => {
        if (!("indexedDB" in window)) {
            reject(new Error("IndexedDB is not supported."));
            return;
        }

        const request = indexedDB.open(DB_NAME, DB_VERSION);

        request.onupgradeneeded = () => {
            const database = request.result;
            if (!database.objectStoreNames.contains(STORE_NAME)) {
                database.createObjectStore(STORE_NAME, { keyPath: "id" });
            }
        };

        request.onsuccess = () => resolve(request.result);
        request.onerror = () => reject(request.error || new Error("Unable to open offline storage."));
    });

    const put = async (id, value) => {
        try {
            const database = await openDatabase();
            await new Promise((resolve, reject) => {
                const transaction = database.transaction(STORE_NAME, "readwrite");
                transaction.objectStore(STORE_NAME).put({ id, ...value });
                transaction.oncomplete = resolve;
                transaction.onerror = () => reject(transaction.error || new Error("Unable to save offline data."));
            });
            database.close();
            return true;
        } catch {
            return false;
        }
    };

    const get = async (id) => {
        try {
            const database = await openDatabase();
            const value = await new Promise((resolve, reject) => {
                const transaction = database.transaction(STORE_NAME, "readonly");
                const request = transaction.objectStore(STORE_NAME).get(id);
                request.onsuccess = () => resolve(request.result || null);
                request.onerror = () => reject(request.error || new Error("Unable to read offline data."));
            });
            database.close();
            return value;
        } catch {
            return null;
        }
    };

    const removeAll = async () => {
        try {
            const database = await openDatabase();
            await new Promise((resolve, reject) => {
                const transaction = database.transaction(STORE_NAME, "readwrite");
                transaction.objectStore(STORE_NAME).clear();
                transaction.oncomplete = resolve;
                transaction.onerror = () => reject(transaction.error || new Error("Unable to clear offline data."));
            });
            database.close();
            return true;
        } catch {
            return false;
        }
    };

    window.PharmaFlowOfflineStore = { put, get, removeAll };
})();