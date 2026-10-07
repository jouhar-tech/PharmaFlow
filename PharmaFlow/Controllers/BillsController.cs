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

        var searchTerm = NormalizeSearch(search);

        var bills = await LoadBillsAsync(
            profileId,
            range.StartUtc,
            range.EndUtc,
            searchTerm,
            cancellationToken);

        var productTerms = await LoadProductTermsAsync(
            bills.Select(b => b.BillId).ToList(),
            cancellationToken);

        var cards = BuildCards(bills, productTerms);

        return View(new BillsViewModel
        {
            SelectedPeriod = period,
            SearchTerm = searchTerm,
            StartDate = period == "Custom" ? startDate?.Date : null,
            EndDate = period == "Custom" ? endDate?.Date : null,
            TotalSalesAmount = bills.Sum(b => b.TotalAmount),
            BillCount = bills.Count,
            Bills = cards
        });
    }

    [HttpGet]
    public async Task<IActionResult> Details(
        long billId,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetProfileId(out var profileId))
            return RedirectToAction("Login", "Account");

        if (billId <= 0)
            return NotFound();

        var bill = await _dbContext.SalesBills
            .AsNoTracking()
            .Where(b =>
                b.BillId == billId &&
                b.ProfileId == profileId &&
                b.Status == "Completed")
            .SingleOrDefaultAsync(cancellationToken);

        if (bill is null)
            return NotFound();

        var items = await _dbContext.SalesBillItems
            .AsNoTracking()
            .Where(i => i.BillId == billId)
            .OrderBy(i => i.BillItemId)
            .Select(i => new BillDetailsLineViewModel
            {
                ProductName = i.ProductName,
                BatchNumber = i.BatchNumber,
                Quantity = i.Quantity,
                LineTotal = i.LineTotal
            })
            .ToListAsync(cancellationToken);

        var profile = await _dbContext.Profiles
            .AsNoTracking()
            .Where(p => p.Id == profileId)
            .Select(p => new
            {
                p.BusinessName,
                p.PhoneNumber,
                p.Email
            })
            .SingleOrDefaultAsync(cancellationToken);

        var details = new BillDetailsViewModel
        {
            BillId = bill.BillId,
            BusinessName = string.IsNullOrWhiteSpace(profile?.BusinessName)
                ? "PharmaFlow"
                : profile.BusinessName!,
            BusinessPhone = profile?.PhoneNumber,
            BusinessEmail = profile?.Email,
            InvoiceNumber = bill.BillNumber,
            InvoiceDate = bill.CreatedAt.AddHours(5.5),
            CustomerName = bill.CustomerName,
            PhoneNumber = bill.CustomerPhone,
            PaymentMethod = string.IsNullOrWhiteSpace(bill.PaymentMethod)
                ? null
                : bill.PaymentMethod,
            TotalAmount = bill.TotalAmount,
            Items = items
        };

        ViewData["Title"] = "Bill Details";
        return View("Details", details);
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

        var searchTerm = NormalizeSearch(q);

        var bills = await LoadBillsAsync(
            profileId,
            range.StartUtc,
            range.EndUtc,
            searchTerm,
            cancellationToken);

        var productTerms = await LoadProductTermsAsync(
            bills.Select(b => b.BillId).ToList(),
            cancellationToken);

        var cards = BuildCards(bills, productTerms);

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
        string search,
        CancellationToken cancellationToken)
    {
        var query = _dbContext.SalesBills
            .AsNoTracking()
            .Where(b => b.ProfileId == profileId && b.Status == "Completed");

        if (startUtc.HasValue)
            query = query.Where(b => b.CreatedAt >= startUtc.Value);

        if (endUtc.HasValue)
            query = query.Where(b => b.CreatedAt < endUtc.Value);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var pattern = $"%{search.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_")}%";

            query = query.Where(b =>
                (b.CustomerName != null && EF.Functions.ILike(b.CustomerName, pattern, "\\"))
                || (b.CustomerPhone != null && EF.Functions.ILike(b.CustomerPhone, pattern, "\\"))
                || _dbContext.SalesBillItems.Any(item =>
                    item.BillId == b.BillId &&
                    EF.Functions.ILike(item.ProductName, pattern, "\\")));
        }

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
