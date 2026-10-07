using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PharmaFlow.Data;
using PharmaFlow.Filters;
using PharmaFlow.Models;
using PharmaFlow.Models.ViewModels;

namespace PharmaFlow.Controllers;

[SessionAuthorize]
public sealed class ReportsController : Controller
{
    private static readonly TimeSpan IndiaOffset = TimeSpan.FromHours(5.5);

    private readonly ApplicationDbContext _dbContext;

    public ReportsController(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    [HttpGet]
    public async Task<IActionResult> Index(
        string period = "7 days",
        DateTime? startDate = null,
        DateTime? endDate = null,
        CancellationToken cancellationToken = default)
    {
        if (!long.TryParse(HttpContext.Session.GetString("ProfileId"), out var profileId))
            return RedirectToAction("Login", "Account");

        if (string.Equals(HttpContext.Session.GetString("UserRole"), "Staff", StringComparison.OrdinalIgnoreCase))
            return RedirectToAction("Index", "Home");

        var allowedPeriods = new[] { "Today", "7 days", "30 days", "All time", "Custom" };
        if (!allowedPeriods.Contains(period, StringComparer.OrdinalIgnoreCase))
            period = "7 days";

        var todayIndia = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, IndiaTimeZone));
        var (rangeStartUtc, rangeEndUtc) = ResolveRange(
            period,
            todayIndia,
            startDate,
            endDate);

        var bills = _dbContext.SalesBills
            .AsNoTracking()
            .Where(b =>
                b.ProfileId == profileId &&
                b.Status == "Completed");

        if (rangeStartUtc.HasValue)
            bills = bills.Where(b => b.CreatedAt >= rangeStartUtc.Value);

        if (rangeEndUtc.HasValue)
            bills = bills.Where(b => b.CreatedAt < rangeEndUtc.Value);

        var billList = await bills
            .OrderByDescending(b => b.CreatedAt)
            .Select(b => new
            {
                b.BillId,
                b.BillNumber,
                b.CreatedAt,
                b.PaymentMethod,
                b.TotalAmount
            })
            .ToListAsync(cancellationToken);

        var billIds = billList.Select(b => b.BillId).ToList();

        var items = billIds.Count == 0
            ? []
            : await _dbContext.SalesBillItems
                .AsNoTracking()
                .Where(i => billIds.Contains(i.BillId))
                .Select(i => new
                {
                    i.BillId,
                    i.ProductName,
                    i.BatchNumber,
                    i.Quantity,
                    i.Mrp,
                    i.PurchaseUnitPrice,
                    i.LineTotal
                })
                .ToListAsync(cancellationToken);

        var billLookup = billList.ToDictionary(b => b.BillId);

        var totalSales = billList.Sum(b => b.TotalAmount);
        var totalMrp = items.Sum(i => i.Mrp * i.Quantity);
        var cashSales = billList.Where(b => b.PaymentMethod == "Cash").Sum(b => b.TotalAmount);
        var upiSales = billList.Where(b => b.PaymentMethod == "UPI").Sum(b => b.TotalAmount);
        var cashUpiTotal = cashSales + upiSales;

        var topSellers = items
            .GroupBy(i => i.ProductName, StringComparer.OrdinalIgnoreCase)
            .Select(group => new TopSellerViewModel
            {
                ProductName = group.First().ProductName,
                QuantitySold = group.Sum(i => i.Quantity),
                Revenue = group.Sum(i => i.LineTotal),
                Mrp = group.Sum(i => i.Mrp * i.Quantity),
                Cost = group.Sum(i => i.PurchaseUnitPrice * i.Quantity),
                Profit = group.Sum(i => i.LineTotal - (i.PurchaseUnitPrice * i.Quantity))
            })
            .OrderByDescending(x => x.Revenue)
            .ThenBy(x => x.ProductName)
            .Take(10)
            .ToList();

        var salesRows = items
            .Select(item =>
            {
                var bill = billLookup[item.BillId];
                return new SalesReportRowViewModel
                {
                    BillNumber = bill.BillNumber,
                    DateTime = new DateTimeOffset(bill.CreatedAt, TimeSpan.Zero).ToOffset(IndiaOffset),
                    ProductName = item.ProductName,
                    BatchNumber = item.BatchNumber,
                    Quantity = item.Quantity,
                    PaymentMode = bill.PaymentMethod,
                    TotalAmount = item.LineTotal,
                    NetProfit = item.LineTotal - (item.PurchaseUnitPrice * item.Quantity)
                };
            })
            .OrderByDescending(row => row.DateTime)
            .ThenBy(row => row.BillNumber)
            .ToList();

        return View(new ReportsViewModel
        {
            SelectedPeriod = period,
            StartDate = startDate?.Date,
            EndDate = endDate?.Date,
            TotalSalesAmount = totalSales,
            Profit = items.Sum(i => i.LineTotal - (i.PurchaseUnitPrice * i.Quantity)),
            BillCount = billList.Count,
            TotalMrp = totalMrp,
            CashPercentage = cashUpiTotal > 0m ? decimal.Round(cashSales / cashUpiTotal * 100m, 1) : 0m,
            UpiPercentage = cashUpiTotal > 0m ? decimal.Round(upiSales / cashUpiTotal * 100m, 1) : 0m,
            TopSellers = topSellers,
            SalesRows = salesRows
        });
    }

    private static (DateTime? StartUtc, DateTime? EndUtc) ResolveRange(
        string period,
        DateOnly todayIndia,
        DateTime? startDate,
        DateTime? endDate)
    {
        DateOnly? startDateIndia = null;
        DateOnly? endDateIndiaExclusive = null;

        switch (period.ToLowerInvariant())
        {
            case "today":
                startDateIndia = todayIndia;
                endDateIndiaExclusive = todayIndia.AddDays(1);
                break;

            case "7 days":
                startDateIndia = todayIndia.AddDays(-6);
                endDateIndiaExclusive = todayIndia.AddDays(1);
                break;

            case "30 days":
                startDateIndia = todayIndia.AddDays(-29);
                endDateIndiaExclusive = todayIndia.AddDays(1);
                break;

            case "custom":
                if (startDate.HasValue &&
                    endDate.HasValue &&
                    startDate.Value.Date <= endDate.Value.Date)
                {
                    startDateIndia = DateOnly.FromDateTime(startDate.Value.Date);
                    endDateIndiaExclusive = DateOnly.FromDateTime(endDate.Value.Date).AddDays(1);
                }
                else
                {
                    startDateIndia = todayIndia.AddDays(-6);
                    endDateIndiaExclusive = todayIndia.AddDays(1);
                }
                break;

            case "all time":
            default:
                break;
        }

        if (!startDateIndia.HasValue || !endDateIndiaExclusive.HasValue)
            return (null, null);

        return (
            ToUtc(startDateIndia.Value),
            ToUtc(endDateIndiaExclusive.Value));
    }

    private static DateTime ToUtc(DateOnly date)
    {
        var local = date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
        return TimeZoneInfo.ConvertTimeToUtc(local, IndiaTimeZone);
    }

    private static TimeZoneInfo IndiaTimeZone =>
        TimeZoneInfo.FindSystemTimeZoneById(
            OperatingSystem.IsWindows() ? "India Standard Time" : "Asia/Kolkata");
}
