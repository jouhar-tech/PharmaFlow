# PharmaHex Android Home-Screen Widget — Implementation Plan

Status: **Web/backend implementation committed; native Android source patch prepared. Android Gradle build and physical-device validation are still pending.**

## Goal

Provide an original PharmaHex Android home-screen widget. Do not copy another pharmacy app's widget design. A pharmacy owner should be able to see today's sales and key operational metrics at a glance, then open PharmaHex directly to create a bill or add stock.

This is a real Android App Widget, not a browser bookmark, PWA shortcut, or a card rendered inside the app.

## User experience

### Add flow

1. Add an **Add to Home screen** row to the More page (PharmaFlow/Views/Profile/Index.cshtml).
2. Tapping the row asks the native Android layer to request pinning of the PharmaHex widget.
3. Android's launcher/system UI displays the widget preview and its Add/Cancel controls. Use the widget provider's preview image/layout so the preview matches the actual widget.
4. If the user confirms Add, Android places the widget on the home screen and the native provider receives the pin/update lifecycle event.
5. If the user cancels, no widget instance or server-side registration is created. Return to More without an error.
6. If the current launcher/device does not support programmatic pinning, show a short guide: long-press the home screen, open Widgets, find PharmaHex, then drag it onto the home screen.

Do not imitate Android's confirmation dialog with an HTML modal and claim that a widget was installed. The launcher must perform the actual pin operation.

### Widget layout

The first supported size is a resizable, wide/medium card with an original PharmaHex composition:

- Deep-teal outer shell with a PharmaHex “P” mark, wordmark, shop name, and update time.
- A light metric board with Today's Sales as the primary value.
- Separate compact tiles for bill count and active alerts.
- Two clear footer actions: **New bill** and **Add stock**.

This deliberately avoids the flat white-card composition from the reference app. Keep the existing PharmaHex teal/mint/neutral palette, strong contrast, rounded shapes, and readable metric hierarchy. Preserve the current RemoteViews IDs so data binding and action wiring remain stable.

### Actions

- **New bill** opens the installed PharmaHex app at /Billing/Create.
- **Add stock** opens /Inventory/Index?openAddStock=1; the Inventory page then opens its existing Add Stock options bottom sheet (Add Custom Product, Search Product, Scan Invoice, Upload Invoice).
- Tapping the non-action body opens /Home/Index.
- These actions must use native PendingIntent/deep-link handling and must not attempt to perform billing or inventory writes directly from RemoteViews.

### Remove flow

Removal is owned by the Android launcher. Users long-press the placed widget and choose **Remove**, as in the supplied reference. The widget provider handles onDeleted() to clear that widget instance's local state. No separate in-app remove button is required for the first version.

## Data and refresh contract

### Summary data

The widget summary endpoint should return only the signed-in profile's data:

- businessName
- todaySalesAmount
- todaySalesBillCount
- alertCount
- updatedAt (ISO-8601 timestamp, displayed in India Standard Time)

todaySalesAmount must use the same date boundary and completed-bill filter as the existing Home dashboard (HomeController.Index).

todaySalesBillCount is a new count using the same profile, status, and IST date boundaries as todaySalesAmount.

For the initial alert definition, count actionable records represented by the Home dashboard's attention sections:
- expiring-soon batches,
- low-stock batches,
- customers with outstanding balance,
- slow-moving products,
- reminders due.

The widget and dashboard must share the same counting/query service so the totals do not drift. Do not infer alert totals from the number of visible cards.

### Endpoint contract

Add an authenticated endpoint such as GET /api/widget/summary that returns the summary above. It must be scoped to one profile and must never accept an arbitrary profileId from the widget request.

The native widget runs outside the WebView. The current ASP.NET login is held in a server-side session cookie, so a native widget cannot assume that it can reuse the WebView's cookie. Before shipping the endpoint, implement a secure widget-session handoff:

1. While the user is signed in inside the app, the web page calls an authenticated endpoint to register/refresh a widget session.
2. The server issues a revocable, short-lived, profile-scoped widget credential (store only a hash server-side).
3. The Median native bridge stores the credential using Android Keystore-backed encrypted storage.
4. The widget worker sends that credential to /api/widget/summary.
5. Logout revokes the widget credential and clears native storage.
6. Never put Supabase service-role keys, database credentials, or long-lived raw secrets in the widget APK, URL, or widget preferences.

The precise credential lifetime, rotation, and storage implementation must be reviewed before release.

### Refresh behavior

- Refresh immediately after a widget is first pinned.
- Refresh when PharmaHex is opened/resumed and after a successful bill or stock update.
- Schedule a best-effort background refresh (target: every 30 minutes) using Android WorkManager. Android may defer background work due to battery/network policies; the timestamp must make stale data visible.
- Cache the last successful response locally. If offline or the server is unavailable, retain the cached values and show the last-updated time rather than replacing them with zero.
- Use a bounded timeout and avoid repeated rapid network requests from multiple widget instances.

## Required native Android integration

The current jouhar-tech/PharmaFlow repository contains the ASP.NET Core web application and its GitHub workflow; it does **not** contain the Median Android source project, Gradle files, Android manifest, or an AppWidgetProvider.

A real widget therefore requires one of these before the user-facing Add option can be enabled:

1. A Median custom Android plugin that exposes a bridge method (proposed name: median.pharmahexWidget.requestPin()), handles the Android AppWidgetManager pin flow, provides widget provider/resources/manifest entries, stores widget credentials securely, and implements widget refresh/actions; or
2. The Android source package downloaded from Median, modified to add the same native components, then rebuilt and republished as a new PharmaHex APK.

The native source patch adds two commands to the existing Median custom bridge: `pharmahexWidget.requestPin` and `pharmahexWidget.refresh`. The More page invokes the first command on Android's main/UI thread, while dashboard lifecycle/bill/stock events invoke the second.

The native implementation needs, at minimum:
- AppWidgetProvider and provider metadata XML,
- widget preview image and RemoteViews layouts for compact/wide sizes,
- native Add/Cancel pin request,
- onUpdate, onDeleted, and onEnabled lifecycle handling,
- WorkManager refresh worker,
- deep-link/PendingIntent handling for the two actions,
- secure widget-session storage and logout cleanup.

The existing ASP.NET GitHub Actions workflow cannot build or install this native component. A successful web build alone is not proof that the Android widget is available.

## Acceptance tests

1. More displays **Add to Home screen** only inside the Median Android app (or explains that the feature is app-only).
2. Tapping it opens the Android widget pin/preview confirmation.
3. Cancel adds nothing and leaves the app state unchanged.
4. Add places a real PharmaHex widget on the launcher.
5. Widget sales amount and bill count match the Home dashboard for the same IST day.
6. Alert count matches the shared dashboard query definition.
7. New bill opens /Billing/Create in PharmaHex.
8. Add stock opens /Inventory/AddItem in PharmaHex.
9. Widget keeps the last successful data and updated timestamp while offline.
10. Long-press → Remove deletes the widget; opening the app later does not recreate it automatically.
11. Logout revokes the widget session and prevents private business data from remaining accessible through subsequent widget refreshes.
12. Test on Samsung One UI and at least one other Android launcher, including compact and resized layouts.

## Source references

- Android App Widgets: https://developer.android.com/develop/ui/views/appwidgets
- Android widget discovery and pinning: https://developer.android.com/design/ui/mobile/guides/widgets/discovery-promotion
- Median native functionality and plugins: https://docs.median.co/docs/native-functionality
- Median custom plugin overview: https://docs.median.co/docs/native-plugins-overview
- Median app architecture and Android source customization: https://docs.median.co/docs/architecture


## Reviewed Median Android source archive (2026-11-25)

The supplied `android_source.tar.gz` was inspected. It is a full Median Android project and contains the native source needed to implement the widget. It is not currently committed as an Android project in this ASP.NET repository.

Verified source details (no credentials reproduced here):
- Android application ID: `co.median.android.odezkww`.
- Firebase project ID in the Android app configuration and Google Services configuration: `pharmahex`; the configured Android package ID matches the Google Services client.
- Minimum SDK 26; target SDK 36; compile SDK 36.
- Java and Kotlin toolchain: 21.
- Gradle wrapper: 9.5.1.
- Median dependencies: `co.median.android:core:2.7.41` and `co.median:median-core:4.2.1`.
- Compiled Median plugins: Firebase Messaging 2.1.0 and Social Login 2.0.0.
- The Firebase Messaging service is active in appConfig; automatic registration is disabled.
- There is no existing AppWidgetProvider, widget metadata XML, widget layout, or widget refresh worker.
- `MainActivity` already routes intent extra `targetUrl` into the WebView. Use this existing path for widget deep actions; do not invent a second deep-link mechanism.
- `GoNativeJSBridgeLibrary.js` exposes `median.nativebridge.custom()`. `MainActivity.runCustomNativeBridge()` dispatches it through `JsCustomCodeExecutor`, whose default handler currently echoes parameters. Add a narrowly scoped widget action there (or through an equivalent native bridge module), preserving the default behavior for all other custom commands.
- The project has a `WebkitCookieManagerProxy` which synchronizes Java networking with Android WebView cookies. A WorkManager worker can use this existing cookie path to call the authenticated widget summary endpoint, rather than embedding database/service-role credentials or issuing a separate long-lived widget secret.
- The archive contains app configuration and Google Services client configuration, but no release/upload keystore file. Gradle refers to `release.keystore` and `upload.keystore` outside the Android project directory. A local debug build can be tested without the production signing key; a production update must use the same signing identity as the installed app. Median's current source-build guide says a Median-provided signing key requires a license and contact with Median support; confirm the signing arrangement before attempting a release build.

### Source-specific native implementation path

1. Add `PharmaHexWidgetProvider` as an `AppWidgetProvider`, provider metadata in `res/xml`, RemoteViews layouts, and a launcher preview image/layout.
2. Add `PharmaHexWidgetManager.requestPin(Activity)` using `AppWidgetManager.isRequestPinAppWidgetSupported()` and `requestPinAppWidget()`. Invoke it only from a foreground Activity, via the existing `median.nativebridge.custom()` command.
3. Keep the Android launcher confirmation authoritative. A successful pin callback is the only reliable confirmation that a widget was added. When the launcher has no pin support, show the manual Widgets-picker instructions in the web UI.
4. Use the existing MainActivity `targetUrl` extra in PendingIntents for `/Home/Index`, `/Billing/Create`, and `/Inventory/AddItem`.
5. Add WorkManager (minimum periodic interval is launcher/OS constrained) to refresh the native RemoteViews. Trigger an immediate refresh on widget creation and app resume; schedule periodic best-effort refresh.
6. Set the existing `WebkitCookieManagerProxy` as the Java CookieHandler inside the worker before calling the authenticated widget summary endpoint. On 401/unauthorized, clear cached business data; on transient network errors, retain the last successful values and show the last-updated timestamp.
7. Implement `onDeleted()` / `onDisabled()` lifecycle handling. Android launcher long-press Remove remains the removal UX; no artificial in-app widget-removal state.
8. Do not change the existing FCM plugin, notification routing, app ID, Firebase project configuration, or package identity as part of this feature.

### Backend summary semantics

The Android widget endpoint must be authenticated by the existing ASP.NET session, derive the profile ID only from that session, and return no user-supplied profile selector.

- Sales amount and bill count use the existing dashboard's completed-bill filter and India Standard Time date boundaries.
- The widget endpoint's `alertCount` includes only two requested inventory categories: active sellable batches expiring today through the next 90 days (inclusive), plus active sellable low-stock batches with quantity below 3. Customer balances, slow-moving products, reminders, and supplier invoice drafts are excluded. It is a count of affected batches; if a batch qualifies for both categories, it contributes once to each category.
- Return the business name and an ISO-8601 updated timestamp. On native offline refresh failure, preserve the previous successful summary instead of showing zeros.

### Repository/build boundary

The ASP.NET web repository currently has no Android module. Keep web changes in the existing PharmaFlow project and maintain the Median Android changes in the supplied Android project until an agreed Android source directory/repository is established. The ASP.NET GitHub Actions build cannot validate Android Gradle changes. Run `./gradlew assembleDebug` from the Android project with JDK 21 and Android SDK 36, then test on a physical Android device/launcher. A successful web build is not evidence that the native widget works.



## Implementation status (2026-11-25)

### ASP.NET repository changes committed

- More page: **Add to Home screen** entry, app-only fallback hint, and native bridge callback.
- `GET /api/widget/summary`: profile-scoped using the existing ASP.NET session; it accepts no profile ID from the caller.
- The summary returns business name, today's completed-bill sales total, today's completed-bill count, actionable alert count, and updated timestamp. Sales calculations use the same India Standard Time day boundaries as the Home dashboard.
- Widget refresh is requested after the app resumes, after a bill is generated, after a successful stock entry, and by the Android periodic worker.
- The latest ASP.NET workflow run for commit `ca2f5609f7a905fa60c33413b2c33bec48f67cb5` passed: https://github.com/jouhar-tech/PharmaFlow/actions/runs/45603256939

### Android source patch prepared locally

The modified Median source archive contains:
- `PharmaHexWidgetProvider`, `PharmaHexWidgetManager`, `PharmaHexWidgetScheduler`, and `PharmaHexWidgetWorker`.
- App widget manifest receiver, provider metadata, RemoteViews layout and launcher preview.
- Real Android launcher pin request; the launcher controls Add/Cancel. If pinning isn't supported, More shows the manual Widgets-picker instructions.
- Widget actions open the existing app through Median's `targetUrl` intent extra.
- WorkManager performs a best-effort refresh every 30 minutes, plus immediate refreshes after pin/update/app resume.
- The worker uses Median's existing `WebkitCookieManagerProxy` to send the signed-in WebView session cookie to the profile-scoped summary endpoint. It stores only the last successful display summary locally, not Supabase service-role credentials or a new long-lived widget token.
- On unauthorized response the widget clears its private cached data; on temporary network failure it keeps the last successful summary and timestamp.
- Launcher long-press Remove is the Android removal UX; the provider stops periodic work and clears cached data when its final widget is removed.

### Validation limits / release blockers

- All Android XML resources and manifest files passed XML parsing; the source wiring, routes, required resources and cookie-proxy references were checked statically.
- The Android Gradle build could not be run in this environment because the Gradle 9.5.1 distribution could not be downloaded (network DNS unavailable), and no Android SDK is installed here. This is **not** a successful Android compilation result.
- The uploaded archive does not include the original release/upload keystore files. Do not sign and distribute an update with a newly generated key: it will not update the currently installed PharmaHex app. Obtain the same release signing identity used for the existing app before making a production APK/AAB.
- The native source patch must be opened/synced in Android Studio with JDK 21 and Android SDK 36, then built and tested on a physical Samsung Android device. Verify launcher Add/Cancel, widget refresh/offline state, deep actions, and long-press Remove before release.
