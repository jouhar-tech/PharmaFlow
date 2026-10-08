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
        var oneYearExpiryLimitDate = today.AddYears(1);

        var oneMonthCutoffUtc = ToIndiaStartUtc(oneMonthCutoffDate, indiaTimeZone);

        // A product is slow-moving when it has remained in inventory for at least
        // one month, still has usable stock, and that stock expires within one year.
        // The stuck amount is calculated only from the qualifying current batches.
        var rows = await _dbContext.Products
            .AsNoTracking()
            .Where(product =>
                product.ProfileId == profileId &&
                product.IsActive &&
                product.CreatedAt <= oneMonthCutoffUtc &&
                product.Batches.Any(batch =>
                    batch.IsActive &&
                    !batch.IsQuarantined &&
                    batch.QuantityOnHand > 0 &&
                    batch.ExpiryDate >= today &&
                    batch.ExpiryDate <= oneYearExpiryLimitDate))
            .Select(product => new
            {
                product.ProductId,
                product.ProductName,
                product.GenericName,
                product.BrandName,
                product.Barcode,
                AddedToInventoryAt = product.CreatedAt,
                Quantity = product.Batches
                    .Where(batch =>
                        batch.IsActive &&
                        !batch.IsQuarantined &&
                        batch.QuantityOnHand > 0 &&
                        batch.ExpiryDate >= today &&
                        batch.ExpiryDate <= oneYearExpiryLimitDate)
                    .Sum(batch => (decimal?)batch.QuantityOnHand) ?? 0m,
                BatchCount = product.Batches.Count(batch =>
                    batch.IsActive &&
                    !batch.IsQuarantined &&
                    batch.QuantityOnHand > 0 &&
                    batch.ExpiryDate >= today &&
                    batch.ExpiryDate <= oneYearExpiryLimitDate),
                StockValue = product.Batches
                    .Where(batch =>
                        batch.IsActive &&
                        !batch.IsQuarantined &&
                        batch.QuantityOnHand > 0 &&
                        batch.ExpiryDate >= today &&
                        batch.ExpiryDate <= oneYearExpiryLimitDate)
                    .Sum(batch => (decimal?)(batch.QuantityOnHand * batch.PurchaseUnitPrice)) ?? 0m,
                EarliestExpiryDate = product.Batches
                    .Where(batch =>
                        batch.IsActive &&
                        !batch.IsQuarantined &&
                        batch.QuantityOnHand > 0 &&
                        batch.ExpiryDate >= today &&
                        batch.ExpiryDate <= oneYearExpiryLimitDate)
                    .Min(batch => (DateOnly?)batch.ExpiryDate)
            })
            .OrderBy(row => row.AddedToInventoryAt)
            .ThenBy(row => row.EarliestExpiryDate)
            .ThenBy(row => row.ProductName)
            .ToListAsync(cancellationToken);

        var items = rows.Select(row =>
        {
            var addedDate = DateOnly.FromDateTime(
                TimeZoneInfo.ConvertTimeFromUtc(
                    DateTime.SpecifyKind(row.AddedToInventoryAt, DateTimeKind.Utc),
                    indiaTimeZone));

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
                AddedToInventoryDate = addedDate,
                DaysInInventory = Math.Max(0, today.DayNumber - addedDate.DayNumber),
                EarliestExpiryDate = row.EarliestExpiryDate!.Value
            };
        }).ToList();

        return View(new SlowMovingViewModel
        {
            Today = today,
            OneMonthCutoffDate = oneMonthCutoffDate,
            ThreeMonthCutoffDate = threeMonthCutoffDate,
            SixMonthCutoffDate = sixMonthCutoffDate,
            OneYearCutoffDate = oneYearCutoffDate,
            OneYearExpiryLimitDate = oneYearExpiryLimitDate,
            AllCount = items.Count,
            ThreeMonthCount = items.Count(item =>
                item.AddedToInventoryDate > threeMonthCutoffDate &&
                item.AddedToInventoryDate <= oneMonthCutoffDate),
            SixMonthCount = items.Count(item =>
                item.AddedToInventoryDate > sixMonthCutoffDate &&
                item.AddedToInventoryDate <= threeMonthCutoffDate),
            OneYearCount = items.Count(item =>
                item.AddedToInventoryDate > oneYearCutoffDate &&
                item.AddedToInventoryDate <= sixMonthCutoffDate),
            TotalStuckAmount = items.Sum(item => item.StockValue),
            Items = items
        });
    }

    private static DateTime ToIndiaStartUtc(DateOnly date, TimeZoneInfo indiaTimeZone) =>
        TimeZoneInfo.ConvertTimeToUtc(
            date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified),
            indiaTimeZone);
}
