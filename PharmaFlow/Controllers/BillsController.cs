using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PharmaFlow.Data;
using PharmaFlow.Filters;
using PharmaFlow.Models;
using PharmaFlow.Models.ViewModels;

namespace PharmaFlow.Controllers;

[SessionAuthorize]
public sealed class BillsController : Controller
{
    private static readonly TimeSpan IndiaOffset = TimeSpan.FromHours(5.5);

    private readonly ApplicationDbContext _dbContext;

    public BillsController(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    [HttpGet]
    public async Task<IActionResult> Index(
        string period = "Today",
        string? search = null,
        DateTime? startDate = null,
        DateTime? endDate = null,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetProfileId(out var profileId))
            return RedirectToAction("Login", "Account");

        period = NormalizePeriod(period);
        var range = ResolveRange(period, startDate, endDate);

        if (period == "Custom" && (!range.StartUtc.HasValue || !range.EndUtc.HasValue))
        {
            period = "Today";
            range = ResolveRange(period, null, null);
        }

        var bills = await LoadBillsAsync(
            profileId,
            range.StartUtc,
            range.EndUtc,
            cancellationToken);

        var searchTerm = NormalizeSearch(search);
        var productTerms = await LoadProductTermsAsync(
            bills.Select(b => b.BillId).ToList(),
            cancellationToken);

        var filteredBills = FilterBills(bills, searchTerm, productTerms);
        var cards = BuildCards(filteredBills, productTerms);

        return View(new BillsViewModel
        {
            SelectedPeriod = period,
            SearchTerm = searchTerm,
            StartDate = period == "Custom" ? startDate?.Date : null,
            EndDate = period == "Custom" ? endDate?.Date : null,
            TotalSalesAmount = filteredBills.Sum(b => b.TotalAmount),
            BillCount = filteredBills.Count,
            Bills = cards
        });
    }

    [HttpGet]
    public async Task<IActionResult> Search(
        string period = "Today",
        string? q = null,
        DateTime? startDate = null,
        DateTime? endDate = null,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetProfileId(out var profileId))
            return Unauthorized();

        period = NormalizePeriod(period);
        var range = ResolveRange(period, startDate, endDate);

        if (period == "Custom" && (!range.StartUtc.HasValue || !range.EndUtc.HasValue))
            return BadRequest();

        var bills = await LoadBillsAsync(
            profileId,
            range.StartUtc,
            range.EndUtc,
            cancellationToken);

        var searchTerm = NormalizeSearch(q);
        var productTerms = await LoadProductTermsAsync(
            bills.Select(b => b.BillId).ToList(),
            cancellationToken);

        var filteredBills = FilterBills(bills, searchTerm, productTerms);
        var cards = BuildCards(filteredBills, productTerms);

        return Ok(new
        {
            totalSalesAmount = cards.Sum(c => c.TotalAmount),
            billCount = cards.Count,
            bills = cards
        });
    }

    private async Task<List<SalesBill>> LoadBillsAsync(
        long profileId,
        DateTime? startUtc,
        DateTime? endUtc,
        CancellationToken cancellationToken)
    {
        var query = _dbContext.SalesBills
            .AsNoTracking()
            .Where(b => b.ProfileId == profileId && b.Status == "Completed");

        if (startUtc.HasValue)
            query = query.Where(b => b.CreatedAt >= startUtc.Value);

        if (endUtc.HasValue)
            query = query.Where(b => b.CreatedAt < endUtc.Value);

        return await query
            .OrderByDescending(b => b.CreatedAt)
            .Take(5000)
            .ToListAsync(cancellationToken);
    }

    private async Task<Dictionary<long, string>> LoadProductTermsAsync(
        IReadOnlyCollection<long> billIds,
        CancellationToken cancellationToken)
    {
        if (billIds.Count == 0)
            return new Dictionary<long, string>();

        var rows = await _dbContext.SalesBillItems
            .AsNoTracking()
            .Where(i => billIds.Contains(i.BillId))
            .Select(i => new { i.BillId, i.ProductName })
            .ToListAsync(cancellationToken);

        return rows
            .GroupBy(x => x.BillId)
            .ToDictionary(
                g => g.Key,
                g => string.Join(", ", g.Select(x => x.ProductName).Distinct(StringComparer.OrdinalIgnoreCase)));
    }

    private static List<SalesBill> FilterBills(
        IReadOnlyList<SalesBill> bills,
        string search,
        IReadOnlyDictionary<long, string> productTerms)
    {
        if (string.IsNullOrWhiteSpace(search))
            return bills.ToList();

        return bills
            .Where(b =>
            {
                productTerms.TryGetValue(b.BillId, out var products);

                return new[]
                {
                    b.CustomerName ?? string.Empty,
                    b.CustomerPhone ?? string.Empty,
                    products ?? string.Empty
                }.Any(value => value.Contains(search, StringComparison.OrdinalIgnoreCase));
            })
            .ToList();
    }

    private static IReadOnlyList<BillCardViewModel> BuildCards(
        IReadOnlyList<SalesBill> bills,
        IReadOnlyDictionary<long, string> productTerms)
    {
        return bills
            .Select(b => new BillCardViewModel
            {
                BillId = b.BillId,
                BillNumber = b.BillNumber,
                CreatedAt = new DateTimeOffset(b.CreatedAt, TimeSpan.Zero).ToOffset(IndiaOffset),
                CustomerName = b.CustomerName,
                CustomerPhone = b.CustomerPhone,
                PaymentMethod = b.PaymentMethod,
                TotalAmount = b.TotalAmount,
                ProductNames = productTerms.TryGetValue(b.BillId, out var products)
                    ? products
                    : string.Empty
            })
            .ToList();
    }

    private static string NormalizePeriod(string? period) =>
        period?.Trim().ToLowerInvariant() switch
        {
            "today" => "Today",
            "last 7 days" => "Last 7 Days",
            "1 month" => "1 Month",
            "custom" => "Custom",
            _ => "Today"
        };

    private static string NormalizeSearch(string? search)
    {
        if (string.IsNullOrWhiteSpace(search))
            return string.Empty;

        var normalized = string.Join(
            " ",
            search.Trim().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

        return normalized.Length <= 80 ? normalized : normalized[..80];
    }

    private static (DateTime? StartUtc, DateTime? EndUtc) ResolveRange(
        string period,
        DateTime? startDate,
        DateTime? endDate)
    {
        var indiaNow = TimeZoneInfo.ConvertTimeFromUtc(
            DateTime.UtcNow,
            IndiaTimeZone);
        var today = DateOnly.FromDateTime(indiaNow);

        DateOnly? start = period switch
        {
            "Today" => today,
            "Last 7 Days" => today.AddDays(-6),
            "1 Month" => today.AddDays(-29),
            "Custom" when startDate.HasValue &&
                         endDate.HasValue &&
                         startDate.Value.Date <= endDate.Value.Date =>
                DateOnly.FromDateTime(startDate.Value.Date),
            _ => null
        };

        DateOnly? endExclusive = period switch
        {
            "Today" => today.AddDays(1),
            "Last 7 Days" => today.AddDays(1),
            "1 Month" => today.AddDays(1),
            "Custom" when startDate.HasValue &&
                         endDate.HasValue &&
                         startDate.Value.Date <= endDate.Value.Date =>
                DateOnly.FromDateTime(endDate.Value.Date).AddDays(1),
            _ => null
        };

        if (!start.HasValue || !endExclusive.HasValue)
            return (null, null);

        return (ToUtc(start.Value), ToUtc(endExclusive.Value));
    }

    private static DateTime ToUtc(DateOnly date)
    {
        var local = date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
        return TimeZoneInfo.ConvertTimeToUtc(local, IndiaTimeZone);
    }

    private static TimeZoneInfo IndiaTimeZone =>
        TimeZoneInfo.FindSystemTimeZoneById(
            OperatingSystem.IsWindows() ? "India Standard Time" : "Asia/Kolkata");

    private bool TryGetProfileId(out long profileId) =>
        long.TryParse(HttpContext.Session.GetString("ProfileId"), out profileId);
}
