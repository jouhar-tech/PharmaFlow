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
            .Where(item => item.Id == profileId)
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
        // The widget only needs the two requested stock-alert categories.
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
                // The widget's Alerts metric intentionally includes only these
                // two inventory conditions: expiry within the next 90 days
                // (including today) and low-stock batches.
                ExpiringSoonCount = group.Count(batch =>
                    batch.ExpiryDate >= today &&
                    batch.ExpiryDate <= ninetyDaysFromToday),
                LowStockCount = group.Count(batch =>
                    batch.QuantityOnHand < 3m)
            })
            .SingleOrDefaultAsync(cancellationToken);

        var expiringSoonCount = stockMetrics?.ExpiringSoonCount ?? 0;
        var lowStockCount = stockMetrics?.LowStockCount ?? 0;
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

        // Keep the widget Alerts count aligned with the two requested categories only.
        // This is a count of affected active batches, not notification-card count.
        var alertCount = expiringSoonCount + lowStockCount;

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
