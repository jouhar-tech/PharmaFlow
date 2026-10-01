# PharmaFlow Push Notifications Setup

## 1. Run the database script

Run both scripts once in the Supabase SQL Editor:

1. `Database/notifications.sql`
2. `Database/notification_cycle.sql`

It creates:
- `push_device_subscriptions` — one row per browser/device subscription.
- `notification_dispatch_log` — prevents duplicate daily/monthly sends.
- `pharmaflow_savings_events` — source-of-truth ledger for real money-saved events.

## 2. Generate VAPID keys once

Create a temporary console app:

```powershell
dotnet new console -n VapidKeyGenerator
cd VapidKeyGenerator
dotnet add package ClosureOSS.WebPush --version 2.5.7
```

Replace Program.cs with:

```csharp
using WebPush;

var keys = VapidHelper.GenerateVapidKeys();

Console.WriteLine($"Public key:  {keys.PublicKey}");
Console.WriteLine($"Private key: {keys.PrivateKey}");
```

Run:

```powershell
dotnet run
```

Store these keys securely. Generate them once and keep using the same pair.

## 3. Configure local development

Use the existing PharmaFlow User Secrets:

```powershell
dotnet user-secrets set "WebPush:Subject" "mailto:your-email@example.com"
dotnet user-secrets set "WebPush:PublicKey" "YOUR_PUBLIC_KEY"
dotnet user-secrets set "WebPush:PrivateKey" "YOUR_PRIVATE_KEY"
```

Do not commit the private key to GitHub.

For production, use environment variables or the hosting provider's secret configuration:

```
WebPush__Subject
WebPush__PublicKey
WebPush__PrivateKey
```

## 4. User permission flow

The dashboard notification bell is the user gesture used to enable browser push notifications. After permission is granted:
1. The browser creates/reuses its PushSubscription.
2. PharmaFlow stores the endpoint and encryption keys against the current profile.
3. A one-time test push is sent to confirm setup.
4. Existing subscriptions are synchronized at most once per 24 hours.

Push requires a service worker and a secure origin (HTTPS in production).

## 5. Daily notification

Every day at exactly 8:30 AM Indian Standard Time, PharmaFlow sends one combined stock notification:

```
Expiring: X products • At-risk value: ₹Y • Low stock: Z products
```

Definitions match the dashboard:
- Expiring products = active, usable stock expiring within 90 days.
- At-risk value = quantity on hand × purchase unit price for that same stock.
- Low stock = active, usable stock with quantity > 0 and < 3.

## 6. Monthly savings notification

On the first day of each month at 8:30 AM IST, PharmaFlow sends the total recorded savings for the completed previous month.

Example:

```
Your recorded PharmaFlow savings for September: ₹12,450.00.
```

Savings are read from `pharmaflow_savings_events`. This avoids inventing a savings number from current inventory. The existing app does not yet have enough billing/payment history to automatically calculate real savings, so the notification remains ₹0 until actual savings events are recorded.

## 7. Production scheduling

The scheduler is implemented as an ASP.NET Core hosted service. It needs the application process to remain running at the scheduled time. On Azure App Service, keep the app warm/Always On so the 8:30 AM job is not skipped during idle shutdown or restarts.

