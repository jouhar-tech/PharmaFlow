using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PharmaFlow.Data;
using PharmaFlow.Filters;
using PharmaFlow.Models;
using PharmaFlow.Models.ViewModels;
using PharmaFlow.Services;

namespace PharmaFlow.Controllers;

[SessionAuthorize]
public sealed class NotificationsController : Controller
{
    private readonly ApplicationDbContext _db;
    private readonly ILogger<NotificationsController> _logger;
    private readonly IPharmaFlowPushNotificationService _notifications;

    public NotificationsController(
        ApplicationDbContext db,
        IPharmaFlowPushNotificationService notifications,
        ILogger<NotificationsController> logger)
    {
        _db = db;
        _notifications = notifications;
        _logger = logger;
    }

    [HttpGet]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        if (!long.TryParse(HttpContext.Session.GetString("ProfileId"), out var profileId))
            return RedirectToAction("Login", "Account");

        var indiaTimeZone = TimeZoneInfo.FindSystemTimeZoneById(
            OperatingSystem.IsWindows() ? "India Standard Time" : "Asia/Kolkata");
        var indiaNow = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, indiaTimeZone);
        var today = DateOnly.FromDateTime(indiaNow);
        var criticalExpiryEnd = today.AddDays(30);
        var upcomingExpiryEnd = today.AddDays(60);
        var alerts = new List<NotificationCenterItemViewModel>();

        // One aggregate query keeps the stock-alert page bounded even when a pharmacy
        // has many batches. Quantities and values are based only on active, sellable stock.
        var activeBatches = _db.ProductBatches
            .AsNoTracking()
            .Where(batch =>
                batch.Product.ProfileId == profileId &&
                batch.Product.IsActive &&
                batch.IsActive &&
                !batch.IsQuarantined &&
                batch.QuantityOnHand > 0);

        var stockStats = await activeBatches
            .GroupBy(_ => 1)
            .Select(group => new
            {
                ExpiredCount = group.Count(batch => batch.ExpiryDate < today),
                ExpiredValue = group
                    .Where(batch => batch.ExpiryDate < today)
                    .Sum(batch => (decimal?)(batch.QuantityOnHand * batch.PurchaseUnitPrice)) ?? 0m,
                CriticalExpiryCount = group.Count(batch =>
                    batch.ExpiryDate >= today && batch.ExpiryDate <= criticalExpiryEnd),
                CriticalExpiryValue = group
                    .Where(batch => batch.ExpiryDate >= today && batch.ExpiryDate <= criticalExpiryEnd)
                    .Sum(batch => (decimal?)(batch.QuantityOnHand * batch.PurchaseUnitPrice)) ?? 0m,
                UpcomingExpiryCount = group.Count(batch =>
                    batch.ExpiryDate > criticalExpiryEnd && batch.ExpiryDate <= upcomingExpiryEnd),
                UpcomingExpiryValue = group
                    .Where(batch => batch.ExpiryDate > criticalExpiryEnd && batch.ExpiryDate <= upcomingExpiryEnd)
                    .Sum(batch => (decimal?)(batch.QuantityOnHand * batch.PurchaseUnitPrice)) ?? 0m,
                LowStockCount = group.Count(batch =>
                    batch.QuantityOnHand > 0 && batch.QuantityOnHand < 3m)
            })
            .SingleOrDefaultAsync(cancellationToken);

        if ((stockStats?.ExpiredCount ?? 0) > 0)
        {
            alerts.Add(new NotificationCenterItemViewModel
            {
                Category = "stock",
                CategoryLabel = "Stock",
                Severity = "critical",
                SeverityLabel = "Critical",
                Icon = "!",
                Title = "Expired stock needs attention",
                Message = $"{stockStats!.ExpiredCount:N0} stock batch(es) have passed their expiry date. Quarantine or remove them before they are sold. Estimated purchase value: {FormatCurrency(stockStats.ExpiredValue)}.",
                AffectedCount = stockStats.ExpiredCount,
                ActionLabel = "Review expired stock",
                ActionUrl = Url.Action("Index", "ExpiryProducts", new { filter = "expired" }) ?? "/ExpiryProducts?filter=expired"
            });
        }

        if ((stockStats?.CriticalExpiryCount ?? 0) > 0)
        {
            alerts.Add(new NotificationCenterItemViewModel
            {
                Category = "stock",
                CategoryLabel = "Stock",
                Severity = "critical",
                SeverityLabel = "Urgent",
                Icon = "⌛",
                Title = "Stock expiring in the next 30 days",
                Message = $"{stockStats!.CriticalExpiryCount:N0} batch(es) are nearing expiry. Review them early to reduce expiry losses. Purchase value at risk: {FormatCurrency(stockStats.CriticalExpiryValue)}.",
                AffectedCount = stockStats.CriticalExpiryCount,
                ActionLabel = "Review expiring stock",
                ActionUrl = Url.Action("Index", "ExpiryProducts", new { filter = "critical" }) ?? "/ExpiryProducts?filter=critical"
            });
        }

        if ((stockStats?.LowStockCount ?? 0) > 0)
        {
            alerts.Add(new NotificationCenterItemViewModel
            {
                Category = "stock",
                CategoryLabel = "Stock",
                Severity = "warning",
                SeverityLabel = "Warning",
                Icon = "↓",
                Title = "Low stock",
                Message = $"{stockStats!.LowStockCount:N0} batch(es) have fewer than 3 units remaining. Check availability and prepare a reorder.",
                AffectedCount = stockStats.LowStockCount,
                ActionLabel = "Open low stock",
                ActionUrl = Url.Action("Index", "LowStock") ?? "/LowStock"
            });
        }

        if ((stockStats?.UpcomingExpiryCount ?? 0) > 0)
        {
            alerts.Add(new NotificationCenterItemViewModel
            {
                Category = "stock",
                CategoryLabel = "Stock",
                Severity = "warning",
                SeverityLabel = "Upcoming",
                Icon = "◷",
                Title = "More stock expires within 31–60 days",
                Message = $"{stockStats!.UpcomingExpiryCount:N0} batch(es) enter the expiry window soon. Purchase value: {FormatCurrency(stockStats.UpcomingExpiryValue)}.",
                AffectedCount = stockStats.UpcomingExpiryCount,
                ActionLabel = "Review upcoming expiry",
                ActionUrl = Url.Action("Index", "ExpiryProducts", new { filter = "upcoming" }) ?? "/ExpiryProducts?filter=upcoming"
            });
        }

        var customerBalances = await _db.CustomerLedgerEntries
            .AsNoTracking()
            .Where(entry => entry.ProfileId == profileId)
            .GroupBy(entry => entry.CustomerId)
            .Select(group => new
            {
                CustomerId = group.Key,
                Balance = group.Sum(entry => entry.BalanceChange)
            })
            .ToListAsync(cancellationToken);

        var customersOwing = customerBalances.Where(row => row.Balance > 0m).ToList();
        var outstandingAmount = customersOwing.Sum(row => row.Balance);

        if (customersOwing.Count > 0)
        {
            alerts.Add(new NotificationCenterItemViewModel
            {
                Category = "customers",
                CategoryLabel = "Customers",
                Severity = "info",
                SeverityLabel = "Follow-up",
                Icon = "₹",
                Title = "Customer Udhaar to collect",
                Message = $"{customersOwing.Count:N0} customer(s) have an outstanding balance totalling {FormatCurrency(outstandingAmount)}. Follow up to keep collections on track.",
                AffectedCount = customersOwing.Count,
                ActionLabel = "View customer balances",
                ActionUrl = Url.Action("Index", "Customers", new { filter = "owed-to-you" }) ?? "/Customers?filter=owed-to-you"
            });
        }

        var dueRemindersCount = await _db.CustomerReminders
            .AsNoTracking()
            .CountAsync(reminder =>
                reminder.ProfileId == profileId &&
                (reminder.Status == "Pending" || reminder.Status == "Ordered") &&
                reminder.ReminderDate.HasValue &&
                reminder.ReminderDate.Value <= today,
                cancellationToken);

        if (dueRemindersCount > 0)
        {
            alerts.Add(new NotificationCenterItemViewModel
            {
                Category = "customers",
                CategoryLabel = "Customers",
                Severity = "warning",
                SeverityLabel = "Due",
                Icon = "↻",
                Title = "Customer reminders are due",
                Message = $"{dueRemindersCount:N0} customer reminder(s) are due today or overdue. Check the follow-up status so no request is missed.",
                AffectedCount = dueRemindersCount,
                ActionLabel = "Open customers",
                ActionUrl = Url.Action("Index", "Customers") ?? "/Customers"
            });
        }

        var upcomingRemindersCount = await _db.CustomerReminders
            .AsNoTracking()
            .CountAsync(reminder =>
                reminder.ProfileId == profileId &&
                (reminder.Status == "Pending" || reminder.Status == "Ordered") &&
                reminder.ReminderDate.HasValue &&
                reminder.ReminderDate.Value > today &&
                reminder.ReminderDate.Value <= today.AddDays(7),
                cancellationToken);

        if (upcomingRemindersCount > 0)
        {
            alerts.Add(new NotificationCenterItemViewModel
            {
                Category = "customers",
                CategoryLabel = "Customers",
                Severity = "info",
                SeverityLabel = "Upcoming",
                Icon = "◷",
                Title = "Customer reminders coming up",
                Message = $"{upcomingRemindersCount:N0} customer reminder(s) are scheduled for the next 7 days.",
                AffectedCount = upcomingRemindersCount,
                ActionLabel = "Open customers",
                ActionUrl = Url.Action("Index", "Customers") ?? "/Customers"
            });
        }

        var draftInvoiceCount = await _db.InvoiceImports
            .AsNoTracking()
            .CountAsync(invoice =>
                invoice.ProfileId == profileId &&
                invoice.Status == "draft",
                cancellationToken);

        if (draftInvoiceCount > 0)
        {
            var latestDraft = await _db.InvoiceImports
                .AsNoTracking()
                .Where(invoice =>
                    invoice.ProfileId == profileId &&
                    invoice.Status == "draft")
                .OrderByDescending(invoice => invoice.UpdatedAt)
                .Select(invoice => new { invoice.ImportId, invoice.OriginalFileName, invoice.DistributorName })
                .FirstOrDefaultAsync(cancellationToken);

            if (latestDraft is not null)
            {
                var sourceName = string.IsNullOrWhiteSpace(latestDraft.DistributorName)
                    ? latestDraft.OriginalFileName
                    : latestDraft.DistributorName;

                alerts.Add(new NotificationCenterItemViewModel
                {
                    Category = "invoices",
                    CategoryLabel = "Supplier invoices",
                    Severity = "warning",
                    SeverityLabel = "Review",
                    Icon = "▤",
                    Title = "Supplier invoice waiting to be saved",
                    Message = draftInvoiceCount == 1
                        ? $"The supplier invoice {sourceName} is still a draft. Review the extracted rows and save it to stock when correct."
                        : $"{draftInvoiceCount:N0} supplier invoice draft(s) are waiting for review. Open the latest draft to continue.",
                    AffectedCount = draftInvoiceCount,
                    ActionLabel = "Review invoice",
                    ActionUrl = Url.Action("Review", "InvoiceCapture", new { id = latestDraft.ImportId }) ?? $"/InvoiceCapture/Review?id={latestDraft.ImportId}"
                });
            }
        }

        var orderedAlerts = alerts
            .OrderBy(alert => alert.Severity switch
            {
                "critical" => 0,
                "warning" => 1,
                _ => 2
            })
            .ThenBy(alert => alert.CategoryLabel, StringComparer.Ordinal)
            .ToList();

        return View(new NotificationCenterViewModel
        {
            GeneratedAt = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, indiaTimeZone),
            Alerts = orderedAlerts
        });
    }

    private static string FormatCurrency(decimal amount) =>
        "₹" + amount.ToString("N2", System.Globalization.CultureInfo.GetCultureInfo("en-IN"));

    [HttpGet]
    public IActionResult PublicKey()
    {
        var publicKey = HttpContext.RequestServices
            .GetRequiredService<IConfiguration>()["WebPush:PublicKey"];

        return string.IsNullOrWhiteSpace(publicKey)
            ? StatusCode(StatusCodes.Status503ServiceUnavailable)
            : Json(new { publicKey });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Subscribe(
        [FromBody] PushSubscriptionRequest? request,
        CancellationToken cancellationToken)
    {
        if (request is null)
            return BadRequest(new { message = "Push subscription data is required." });

        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        if (!Uri.TryCreate(request.Endpoint, UriKind.Absolute, out var endpoint) ||
            endpoint.Scheme != Uri.UriSchemeHttps)
            return BadRequest(new { message = "Invalid push endpoint." });

        if (request.P256dh.Length is < 20 or > 500 ||
            request.Auth.Length is < 10 or > 500)
            return BadRequest(new { message = "Invalid push subscription keys." });

        if (!long.TryParse(HttpContext.Session.GetString("ProfileId"), out var profileId))
            return Unauthorized();

        var profileActive = await _db.Profiles
            .AsNoTracking()
            .AnyAsync(p => p.Id == profileId && p.ActiveStatus == 1, cancellationToken);

        if (!profileActive)
            return Unauthorized();

        var role = HttpContext.Session.GetString("UserRole") ?? "Owner";
        var now = DateTime.UtcNow;

        var existing = await _db.PushDeviceSubscriptions
            .SingleOrDefaultAsync(s => s.Endpoint == request.Endpoint, cancellationToken);

        if (existing is null)
        {
            _db.PushDeviceSubscriptions.Add(new PushDeviceSubscription
            {
                ProfileId = profileId,
                UserRole = role,
                Endpoint = request.Endpoint,
                P256dh = request.P256dh,
                Auth = request.Auth,
                UserAgent = Request.Headers.UserAgent.ToString(),
                IsActive = true,
                CreatedAt = now,
                UpdatedAt = now
            });
        }
        else
        {
            existing.ProfileId = profileId;
            existing.UserRole = role;
            existing.P256dh = request.P256dh;
            existing.Auth = request.Auth;
            existing.UserAgent = Request.Headers.UserAgent.ToString();
            existing.IsActive = true;
            existing.UpdatedAt = now;
        }

        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save push subscription for profile {ProfileId}.", profileId);
            return StatusCode(StatusCodes.Status503ServiceUnavailable,
                new { message = "Notification subscription could not be saved. Please try again." });
        }

        return NoContent();
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Unsubscribe(
        [FromBody] UnsubscribeRequest? request,
        CancellationToken cancellationToken)
    {
        if (request is null)
            return BadRequest(new { message = "Push subscription data is required." });

        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        if (!long.TryParse(HttpContext.Session.GetString("ProfileId"), out var profileId))
            return Unauthorized();

        try
        {
            await _db.PushDeviceSubscriptions
                .Where(s => s.ProfileId == profileId && s.Endpoint == request.Endpoint)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(s => s.IsActive, false)
                    .SetProperty(s => s.UpdatedAt, DateTime.UtcNow),
                    cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to unsubscribe push device for profile {ProfileId}.", profileId);
            return StatusCode(StatusCodes.Status503ServiceUnavailable,
                new { message = "Notification subscription could not be removed. Please try again." });
        }

        return NoContent();
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Test(CancellationToken cancellationToken)
    {
        if (!long.TryParse(HttpContext.Session.GetString("ProfileId"), out var profileId))
            return Unauthorized();

        var sent = await _notifications.SendTestNotificationAsync(profileId, cancellationToken);
        return sent ? Ok() : StatusCode(StatusCodes.Status503ServiceUnavailable);
    }

    public sealed class PushSubscriptionRequest
    {
        [Required, StringLength(2000)]
        public string Endpoint { get; set; } = string.Empty;

        [Required, StringLength(500)]
        public string P256dh { get; set; } = string.Empty;

        [Required, StringLength(500)]
        public string Auth { get; set; } = string.Empty;
    }

    public sealed class UnsubscribeRequest
    {
        [Required, StringLength(2000)]
        public string Endpoint { get; set; } = string.Empty;
    }
}
