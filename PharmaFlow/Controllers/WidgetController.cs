using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PharmaFlow.Data;

namespace PharmaFlow.Controllers;

/// <summary>
/// Returns a minimal, profile-scoped summary for the installed PharmaHex Android
/// home-screen widget. Authentication is the existing server-side ASP.NET session
/// cookie shared by the Median WebView; no profile selector is accepted from clients.
/// </summary>
[ApiController]
[Route("api/widget")]
public sealed class WidgetController : ControllerBase
{
    private readonly ApplicationDbContext _dbContext;

    public WidgetController(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    [HttpGet("summary")]
    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public async Task<IActionResult> Summary(CancellationToken cancellationToken)
    {
        var accessToken = HttpContext.Session.GetString("SupabaseAccessToken");
        var userId = HttpContext.Session.GetString("SupabaseUserId");
        var profileIdText = HttpContext.Session.GetString("ProfileId");

        if (string.IsNullOrWhiteSpace(accessToken) ||
            !Guid.TryParse(userId, out _) ||
            !long.TryParse(profileIdText, out var profileId))
        {
            return Unauthorized();
        }

        var profile = await _dbContext.Profiles
            .AsNoTracking()
            .Where(item => item.Id == profileId && item.ActiveStatus == 1)
            .Select(item => new { item.BusinessName })
            .SingleOrDefaultAsync(cancellationToken);

        if (profile is null)
            return Unauthorized();

        var indiaTimeZone = TimeZoneInfo.FindSystemTimeZoneById(
            OperatingSystem.IsWindows() ? "India Standard Time" : "Asia/Kolkata");
        var indiaNow = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, indiaTimeZone);
        var today = DateOnly.FromDateTime(indiaNow);
        var ninetyDaysFromToday = today.AddDays(90);

        var indiaStartUtc = TimeZoneInfo.ConvertTimeToUtc(
            today.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified),
            indiaTimeZone);
        var indiaEndUtc = TimeZoneInfo.ConvertTimeToUtc(
            today.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified),
            indiaTimeZone);
        var slowMovingCutoffDate = today.AddMonths(-1);
        var slowMovingCutoffUtc = TimeZoneInfo.ConvertTimeToUtc(
            slowMovingCutoffDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified),
            indiaTimeZone);

        // Keep the widget's expiring, low-stock and slow-moving counts identical
        // to the dashboard's current business rules.
        var stockMetrics = await _dbContext.ProductBatches
            .AsNoTracking()
            .Where(batch =>
                batch.Product.ProfileId == profileId &&
                batch.Product.IsActive &&
                batch.IsActive &&
                !batch.IsQuarantined &&
                batch.QuantityOnHand > 0)
            .GroupBy(_ => 1)
            .Select(group => new
            {
                ExpiringSoonCount = group.Count(batch =>
                    batch.ExpiryDate >= today &&
                    batch.ExpiryDate <= ninetyDaysFromToday),
                LowStockCount = group.Count(batch =>
                    batch.QuantityOnHand < 3m),
                SlowMovingStockCount = group
                    .Where(batch =>
                        !_dbContext.SalesBillItems.Any(item =>
                            item.ProductId == batch.ProductId &&
                            item.Bill.ProfileId == profileId &&
                            item.Bill.Status == "Completed" &&
                            item.Bill.CreatedAt > slowMovingCutoffUtc))
                    .Select(batch => batch.ProductId)
                    .Distinct()
                    .Count()
            })
            .SingleOrDefaultAsync(cancellationToken);

        var expiringSoonCount = stockMetrics?.ExpiringSoonCount ?? 0;
        var lowStockCount = stockMetrics?.LowStockCount ?? 0;
        var slowMovingStockCount = stockMetrics?.SlowMovingStockCount ?? 0;

        var todaySalesQuery = _dbContext.SalesBills
            .AsNoTracking()
            .Where(bill =>
                bill.ProfileId == profileId &&
                bill.Status == "Completed" &&
                bill.CreatedAt >= indiaStartUtc &&
                bill.CreatedAt < indiaEndUtc);

        var todaySalesAmount = await todaySalesQuery
            .SumAsync(bill => bill.TotalAmount, cancellationToken);
        var todaySalesBillCount = await todaySalesQuery
            .CountAsync(cancellationToken);

        var customerBalances = await _dbContext.CustomerLedgerEntries
            .AsNoTracking()
            .Where(entry => entry.ProfileId == profileId)
            .GroupBy(entry => entry.CustomerId)
            .Select(group => group.Sum(entry => entry.BalanceChange))
            .ToListAsync(cancellationToken);

        var customersWithOutstanding = customerBalances.Count(balance => balance > 0m);

        var remindersDueCount = await _dbContext.CustomerReminders
            .AsNoTracking()
            .CountAsync(reminder =>
                reminder.ProfileId == profileId &&
                (reminder.Status == "Pending" || reminder.Status == "Ordered") &&
                reminder.ReminderDate.HasValue &&
                reminder.ReminderDate.Value <= today,
                cancellationToken);

        // Alerts represent actionable records, using the same counts displayed
        // in the dashboard's What Needs Your Attention section.
        var alertCount =
            expiringSoonCount +
            lowStockCount +
            customersWithOutstanding +
            slowMovingStockCount +
            remindersDueCount;

        return Ok(new
        {
            businessName = string.IsNullOrWhiteSpace(profile.BusinessName)
                ? "Your shop"
                : profile.BusinessName,
            todaySalesAmount,
            todaySalesBillCount,
            alertCount,
            updatedAt = DateTimeOffset.UtcNow
        });
    }
}
