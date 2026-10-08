using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PharmaFlow.Data;
using PharmaFlow.Filters;
using PharmaFlow.Models.ViewModels;

namespace PharmaFlow.Controllers;

[SessionAuthorize]
public sealed class SlowMovingController : Controller
{
    private readonly ApplicationDbContext _dbContext;

    public SlowMovingController(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        if (!long.TryParse(HttpContext.Session.GetString("ProfileId"), out var profileId))
            return RedirectToAction("Login", "Account");

        var indiaTimeZone = TimeZoneInfo.FindSystemTimeZoneById(
            OperatingSystem.IsWindows() ? "India Standard Time" : "Asia/Kolkata");
        var today = DateOnly.FromDateTime(
            TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, indiaTimeZone));

        var oneMonthCutoffDate = today.AddMonths(-1);
        var threeMonthCutoffDate = today.AddMonths(-3);
        var sixMonthCutoffDate = today.AddMonths(-6);
        var oneYearCutoffDate = today.AddYears(-1);

        var oneMonthCutoffUtc = ToIndiaStartUtc(oneMonthCutoffDate, indiaTimeZone);

        // Load only products with usable current stock. The last sale is calculated
        // at product level across completed bills, then the database filters to the
        // slow-moving set before rows are materialized.
        var rows = await _dbContext.Products
            .AsNoTracking()
            .Where(product =>
                product.ProfileId == profileId &&
                product.IsActive &&
                product.Batches.Any(batch =>
                    batch.IsActive &&
                    !batch.IsQuarantined &&
                    batch.QuantityOnHand > 0))
            .Select(product => new
            {
                product.ProductId,
                product.ProductName,
                product.GenericName,
                product.BrandName,
                product.Barcode,
                Quantity = product.Batches
                    .Where(batch =>
                        batch.IsActive &&
                        !batch.IsQuarantined &&
                        batch.QuantityOnHand > 0)
                    .Sum(batch => (decimal?)batch.QuantityOnHand) ?? 0m,
                BatchCount = product.Batches.Count(batch =>
                    batch.IsActive &&
                    !batch.IsQuarantined &&
                    batch.QuantityOnHand > 0),
                StockValue = product.Batches
                    .Where(batch =>
                        batch.IsActive &&
                        !batch.IsQuarantined &&
                        batch.QuantityOnHand > 0)
                    .Sum(batch => (decimal?)(batch.QuantityOnHand * batch.PurchaseUnitPrice)) ?? 0m,
                LastSoldAt = _dbContext.SalesBillItems
                    .Where(item =>
                        item.ProductId == product.ProductId &&
                        item.Bill.ProfileId == profileId &&
                        item.Bill.Status == "Completed")
                    .Select(item => (DateTime?)item.Bill.CreatedAt)
                    .Max()
            })
            .Where(row =>
                row.LastSoldAt == null ||
                row.LastSoldAt <= oneMonthCutoffUtc)
            .OrderBy(row => row.LastSoldAt ?? DateTime.MinValue)
            .ThenBy(row => row.ProductName)
            .ToListAsync(cancellationToken);

        var items = rows.Select(row =>
        {
            DateOnly? lastSoldDate = null;
            int? daysSinceSale = null;

            if (row.LastSoldAt.HasValue)
            {
                var soldAtUtc = DateTime.SpecifyKind(row.LastSoldAt.Value, DateTimeKind.Utc);
                var soldAtIndia = TimeZoneInfo.ConvertTimeFromUtc(soldAtUtc, indiaTimeZone);
                lastSoldDate = DateOnly.FromDateTime(soldAtIndia);
                daysSinceSale = Math.Max(0, today.DayNumber - lastSoldDate.Value.DayNumber);
            }

            return new SlowMovingProductItemViewModel
            {
                ProductId = row.ProductId,
                ProductName = row.ProductName,
                GenericName = row.GenericName,
                BrandName = row.BrandName,
                Barcode = row.Barcode,
                Quantity = row.Quantity,
                BatchCount = row.BatchCount,
                StockValue = row.StockValue,
                LastSoldDate = lastSoldDate,
                DaysSinceSale = daysSinceSale,
                NeverSold = !lastSoldDate.HasValue
            };
        }).ToList();

        return View(new SlowMovingViewModel
        {
            Today = today,
            OneMonthCutoffDate = oneMonthCutoffDate,
            ThreeMonthCutoffDate = threeMonthCutoffDate,
            SixMonthCutoffDate = sixMonthCutoffDate,
            OneYearCutoffDate = oneYearCutoffDate,
            AllCount = items.Count,
            ThreeMonthCount = items.Count(item =>
                item.LastSoldDate.HasValue &&
                item.LastSoldDate.Value > threeMonthCutoffDate &&
                item.LastSoldDate.Value <= oneMonthCutoffDate),
            SixMonthCount = items.Count(item =>
                item.LastSoldDate.HasValue &&
                item.LastSoldDate.Value > sixMonthCutoffDate &&
                item.LastSoldDate.Value <= threeMonthCutoffDate),
            OneYearCount = items.Count(item =>
                item.LastSoldDate.HasValue &&
                item.LastSoldDate.Value > oneYearCutoffDate &&
                item.LastSoldDate.Value <= sixMonthCutoffDate),
            Items = items
        });
    }

    private static DateTime ToIndiaStartUtc(DateOnly date, TimeZoneInfo indiaTimeZone) =>
        TimeZoneInfo.ConvertTimeToUtc(
            date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified),
            indiaTimeZone);
}
