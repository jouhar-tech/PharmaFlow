using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PharmaFlow.Data;
using PharmaFlow.Models;
using WebPush;

namespace PharmaFlow.Services;

public interface IPharmaFlowPushNotificationService
{
    Task<bool> SendDailyStockNotificationAsync(long profileId, DateOnly localDate, CancellationToken cancellationToken);
    Task<bool> SendMonthlySavingsNotificationAsync(long profileId, DateOnly monthStart, CancellationToken cancellationToken);
    Task<bool> SendTestNotificationAsync(long profileId, CancellationToken cancellationToken);
}

public sealed class PharmaFlowPushNotificationService : IPharmaFlowPushNotificationService
{
    private const string DailyNotificationType = "daily-stock";
    private const string MonthlyNotificationType = "monthly-savings";

    private readonly ApplicationDbContext _db;
    private readonly WebPushClient _webPushClient;
    private readonly IConfiguration _configuration;
    private readonly ILogger<PharmaFlowPushNotificationService> _logger;

    public PharmaFlowPushNotificationService(
        ApplicationDbContext db,
        WebPushClient webPushClient,
        IConfiguration configuration,
        ILogger<PharmaFlowPushNotificationService> logger)
    {
        _db = db;
        _webPushClient = webPushClient;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task<bool> SendDailyStockNotificationAsync(
        long profileId,
        DateOnly localDate,
        CancellationToken cancellationToken)
    {
        if (!await HasActiveSubscriptionAsync(profileId, cancellationToken))
            return false;

        var stats = await GetStockStatsAsync(profileId, localDate, cancellationToken);

        var periodKey = localDate.ToString("yyyy-MM-dd");
        if (!await TryClaimDispatchAsync(profileId, DailyNotificationType, periodKey, cancellationToken))
            return false;

        var body =
            $"Expiring: {stats.ExpiringCount} products • " +
            $"At-risk value: ₹{stats.ExpiringValue.ToString("N2", System.Globalization.CultureInfo.GetCultureInfo("en-IN"))} • " +
            $"Low stock: {stats.LowStockCount} products";

        return await SendToProfileAsync(
            profileId,
            "PharmaFlow Daily Stock Check",
            body,
            "/Home",
            cancellationToken);
    }

    public async Task<bool> SendMonthlySavingsNotificationAsync(
        long profileId,
        DateOnly monthStart,
        CancellationToken cancellationToken)
    {
        if (!await HasActiveSubscriptionAsync(profileId, cancellationToken))
            return false;

        var monthEnd = monthStart.AddMonths(1);
        var india = NotificationTimeZone.GetIndiaStandardTimeZone();
        var startUtc = TimeZoneInfo.ConvertTimeToUtc(
            DateTime.SpecifyKind(monthStart.ToDateTime(TimeOnly.MinValue), DateTimeKind.Unspecified),
            india);
        var endUtc = TimeZoneInfo.ConvertTimeToUtc(
            DateTime.SpecifyKind(monthEnd.ToDateTime(TimeOnly.MinValue), DateTimeKind.Unspecified),
            india);

        var savings = await _db.PharmaFlowSavingsEvents
            .AsNoTracking()
            .Where(e =>
                e.ProfileId == profileId &&
                e.OccurredAt >= startUtc &&
                e.OccurredAt < endUtc)
            .SumAsync(e => (decimal?)e.Amount, cancellationToken) ?? 0m;

        var periodKey = monthStart.ToString("yyyy-MM");
        if (!await TryClaimDispatchAsync(profileId, MonthlyNotificationType, periodKey, cancellationToken))
            return false;

        var monthName = monthStart.ToDateTime(TimeOnly.MinValue).ToString("MMMM");
        var body =
            $"Your recorded PharmaFlow savings for {monthName}: " +
            $"₹{savings.ToString("N2", System.Globalization.CultureInfo.GetCultureInfo("en-IN"))}.";

        return await SendToProfileAsync(
            profileId,
            "PharmaFlow Monthly Savings",
            body,
            "/Home",
            cancellationToken);
    }

    public async Task<bool> SendTestNotificationAsync(
        long profileId,
        CancellationToken cancellationToken)
    {
        return await SendToProfileAsync(
            profileId,
            "PharmaFlow Notifications Enabled",
            "Your PharmaFlow notifications are working. Daily stock alerts will arrive at 8:30 AM.",
            "/Home",
            cancellationToken);
    }

    private async Task<(int ExpiringCount, decimal ExpiringValue, int LowStockCount)> GetStockStatsAsync(
        long profileId,
        DateOnly localDate,
        CancellationToken cancellationToken)
    {
        var expiryCutoff = localDate.AddDays(90);

        var stats = await _db.ProductBatches
            .AsNoTracking()
            .Where(b =>
                b.Product.ProfileId == profileId &&
                b.Product.IsActive &&
                b.IsActive &&
                !b.IsQuarantined &&
                b.QuantityOnHand > 0)
            .GroupBy(_ => 1)
            .Select(g => new
            {
                ExpiringCount = g.Sum(b =>
                    b.ExpiryDate >= localDate && b.ExpiryDate <= expiryCutoff ? 1 : 0),
                ExpiringValue = g.Sum(b =>
                    b.ExpiryDate >= localDate && b.ExpiryDate <= expiryCutoff
                        ? b.QuantityOnHand * b.PurchaseUnitPrice
                        : 0m),
                LowStockCount = g.Sum(b =>
                    b.QuantityOnHand < 3m ? 1 : 0)
            })
            .SingleOrDefaultAsync(cancellationToken);

        return stats is null
            ? (0, 0m, 0)
            : (stats.ExpiringCount, stats.ExpiringValue, stats.LowStockCount);
    }

    private async Task<bool> HasActiveSubscriptionAsync(
        long profileId,
        CancellationToken cancellationToken)
    {
        return await _db.PushDeviceSubscriptions
            .AsNoTracking()
            .AnyAsync(
                s => s.ProfileId == profileId && s.IsActive,
                cancellationToken);
    }

    private async Task<bool> TryClaimDispatchAsync(
        long profileId,
        string notificationType,
        string periodKey,
        CancellationToken cancellationToken)
    {
        var affected = await _db.Database.ExecuteSqlInterpolatedAsync(
            $"""
            INSERT INTO public.notification_dispatch_log
                (profile_id, notification_type, period_key, sent_at)
            VALUES
                ({profileId}, {notificationType}, {periodKey}, NOW())
            ON CONFLICT (profile_id, notification_type, period_key)
            DO NOTHING;
            """,
            cancellationToken);

        return affected > 0;
    }

    private async Task<bool> SendToProfileAsync(
        long profileId,
        string title,
        string body,
        string url,
        CancellationToken cancellationToken)
    {
        var subject = _configuration["WebPush:Subject"];
        var publicKey = _configuration["WebPush:PublicKey"];
        var privateKey = _configuration["WebPush:PrivateKey"];

        if (string.IsNullOrWhiteSpace(subject) ||
            string.IsNullOrWhiteSpace(publicKey) ||
            string.IsNullOrWhiteSpace(privateKey))
        {
            _logger.LogError("Web Push VAPID configuration is missing.");
            return false;
        }

        var subscriptions = await _db.PushDeviceSubscriptions
            .Where(s => s.ProfileId == profileId && s.IsActive)
            .ToListAsync(cancellationToken);

        if (subscriptions.Count == 0)
            return false;

        var vapid = new VapidDetails(subject, publicKey, privateKey);
        var options = new WebPushOptions
        {
            VapidDetails = vapid,
            ContentEncoding = ContentEncoding.Aes128gcm,
            Urgency = Urgency.Normal
        };

        var payload = JsonSerializer.Serialize(new
        {
            title,
            body,
            url
        });

        var delivered = false;

        foreach (var item in subscriptions)
        {
            try
            {
                var subscription = new PushSubscription(
                    item.Endpoint,
                    item.P256dh,
                    item.Auth);

                await _webPushClient.SendNotificationAsync(
                    subscription,
                    payload,
                    options,
                    cancellationToken);

                item.LastSuccessAt = DateTime.UtcNow;
                item.UpdatedAt = DateTime.UtcNow;
                delivered = true;
            }
            catch (WebPushException ex) when (
                ex.StatusCode == HttpStatusCode.Gone ||
                ex.StatusCode == HttpStatusCode.NotFound)
            {
                item.IsActive = false;
                item.LastFailureAt = DateTime.UtcNow;
                item.UpdatedAt = DateTime.UtcNow;
                _logger.LogInformation(
                    "Removed expired push subscription {SubscriptionId} for profile {ProfileId}.",
                    item.SubscriptionId,
                    profileId);
            }
            catch (Exception ex)
            {
                item.LastFailureAt = DateTime.UtcNow;
                item.UpdatedAt = DateTime.UtcNow;
                _logger.LogWarning(
                    ex,
                    "Push delivery failed for subscription {SubscriptionId}, profile {ProfileId}.",
                    item.SubscriptionId,
                    profileId);
            }
        }

        await _db.SaveChangesAsync(cancellationToken);
        return delivered;
    }
}

internal static class NotificationTimeZone
{
    public static TimeZoneInfo GetIndiaStandardTimeZone()
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById("Asia/Kolkata");
        }
        catch (TimeZoneNotFoundException)
        {
            return TimeZoneInfo.FindSystemTimeZoneById("India Standard Time");
        }
    }
}
