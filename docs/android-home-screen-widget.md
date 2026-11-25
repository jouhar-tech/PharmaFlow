# PharmaHex Android Home-Screen Widget — Implementation Plan

Status: **Design approved for implementation planning; native integration not yet implemented.**

## Goal

Provide an Android home-screen widget similar to the supplied MedCity reference. A pharmacy owner should be able to see today's sales and outstanding attention items at a glance, then open PharmaHex directly to create a bill or add stock.

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

The first supported size should be a resizable, wide/medium card:

- Header: PharmaHex mark, shop/business name, and last-updated time.
- Main metric: **Today's Sales** amount.
- Secondary metrics: bill count and an **Alerts** count.
- Primary action: **New bill**.
- Secondary action: **Add stock**.

The widget should use the existing PharmaHex visual language: white card, deep teal primary action, pale teal secondary action, rounded corners, and clear contrast. Provide a compact layout for smaller widget widths and a wider layout for the reference-style card.

### Actions

- **New bill** opens the installed PharmaHex app at /Billing/Create.
- **Add stock** opens the installed PharmaHex app at /Inventory/AddItem.
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

The proposed bridge method name is a contract for a future custom plugin; it is **not** an existing Median JavaScript Bridge API and must not be called until the native plugin is actually added.

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
