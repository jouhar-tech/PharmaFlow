using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PharmaFlow.Data;
using PharmaFlow.Filters;
using PharmaFlow.Models.ViewModels;

namespace PharmaFlow.Controllers;

[SessionAuthorize]
public sealed class InventoryController : Controller
{
    private readonly ApplicationDbContext _dbContext;

    public InventoryController(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        if (!long.TryParse(HttpContext.Session.GetString("ProfileId"), out var profileId))
            return RedirectToAction("Login", "Account");

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var ninetyDaysFromToday = today.AddDays(90);

        var rows = await _dbContext.ProductBatches
            .AsNoTracking()
            .Where(batch =>
                batch.Product.ProfileId == profileId &&
                batch.Product.IsActive &&
                batch.IsActive &&
                !batch.IsQuarantined &&
                batch.QuantityOnHand > 0)
            .OrderBy(batch => batch.Product.ProductName)
            .ThenBy(batch => batch.ExpiryDate)
            .Select(batch => new
            {
                ProductId = batch.ProductId,
                BatchId = batch.BatchId,
                ProductName = batch.Product.ProductName,
                GenericName = batch.Product.GenericName,
                BrandName = batch.Product.BrandName,
                Barcode = batch.Product.Barcode,
                BatchNumber = batch.BatchNumber,
                Quantity = batch.QuantityOnHand,
                ReorderLevel = batch.Product.ReorderLevel,
                ExpiryDate = batch.ExpiryDate,
                SellingUnitPrice = batch.SellingUnitPrice
            })
            .ToListAsync(cancellationToken);

        var items = rows.Select(row =>
        {
            var daysLeft = row.ExpiryDate.DayNumber - today.DayNumber;
            var isLowStock = row.ReorderLevel > 0 && row.Quantity <= row.ReorderLevel;
            var isExpiring = daysLeft >= 0 && daysLeft <= 90;

            return new InventoryItemViewModel
            {
                ProductId = row.ProductId,
                BatchId = row.BatchId,
                ProductName = row.ProductName,
                GenericName = row.GenericName,
                BrandName = row.BrandName,
                Barcode = row.Barcode,
                BatchNumber = row.BatchNumber,
                Quantity = row.Quantity,
                ReorderLevel = row.ReorderLevel,
                ExpiryDate = row.ExpiryDate,
                DaysLeft = daysLeft,
                SellingUnitPrice = row.SellingUnitPrice,
                StockValue = row.Quantity * row.SellingUnitPrice,
                IsLowStock = isLowStock,
                IsExpiring = isExpiring
            };
        }).ToList();

        return View(new InventoryViewModel
        {
            AllCount = items.Count,
            LowStockCount = items.Count(item => item.IsLowStock),
            ExpiringCount = items.Count(item => item.IsExpiring),
            Items = items
        });
    }
}
