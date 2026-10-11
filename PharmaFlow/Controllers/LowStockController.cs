using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Npgsql.EntityFrameworkCore.PostgreSQL;
using PharmaFlow.Data;
using PharmaFlow.Filters;
using PharmaFlow.Models;
using PharmaFlow.Models.ViewModels;

namespace PharmaFlow.Controllers;

[SessionAuthorize]
public sealed class LowStockController : Controller
{
    private readonly ApplicationDbContext _dbContext;

    public LowStockController(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        if (!long.TryParse(HttpContext.Session.GetString("ProfileId"), out var profileId))
            return RedirectToAction("Login", "Account");

        // Low-stock listing is read-only; use a bounded PostgreSQL retry strategy
        // so transient connection failures do not render the page unusable.
        var lowStockExecutionStrategy = new NpgsqlRetryingExecutionStrategy(
            _dbContext,
            maxRetryCount: 3);

        var rows = await lowStockExecutionStrategy.ExecuteAsync(
            () => _dbContext.ProductBatches
                .AsNoTracking()
                .Where(batch =>
                    batch.Product.ProfileId == profileId &&
                    batch.Product.IsActive &&
                    batch.IsActive &&
                    !batch.IsQuarantined &&
                    batch.QuantityOnHand > 0 &&
                    batch.QuantityOnHand < 3)
                .OrderBy(batch => batch.Product.ProductName)
                .ThenBy(batch => batch.QuantityOnHand)
                .ThenBy(batch => batch.ExpiryDate)
                .Select(batch => new
                {
                    ProductId = batch.ProductId,
                    BatchId = batch.BatchId,
                    CatalogId = batch.Product.CatalogId,
                    ProductName = batch.Product.ProductName,
                    GenericName = batch.Product.GenericName,
                    BrandName = batch.Product.BrandName,
                    Barcode = batch.Product.Barcode,
                    BatchNumber = batch.BatchNumber,
                    Quantity = batch.QuantityOnHand,
                    ExpiryDate = batch.ExpiryDate,
                    SellingUnitPrice = batch.SellingUnitPrice
                })
                .ToListAsync(cancellationToken));

        // Load this pharmacy's saved reorder entries so the button state survives
        // navigation away from Low Stock and back again.
        var savedReorderItems = await _dbContext.ReorderListItems
            .AsNoTracking()
            .Include(item => item.Product)
            .Where(item => item.ProfileId == profileId)
            .ToListAsync(cancellationToken);

        var items = rows.Select(row =>
        {
            var savedReorderItem = savedReorderItems.FirstOrDefault(saved =>
                saved.ProductId == row.ProductId ||
                (!saved.ProductId.HasValue &&
                    ((row.CatalogId.HasValue &&
                        (saved.CatalogId == row.CatalogId ||
                         saved.Product != null && saved.Product.CatalogId == row.CatalogId)) ||
                     (!string.IsNullOrWhiteSpace(row.Barcode) &&
                        ((saved.Barcode != null &&
                          string.Equals(saved.Barcode.Trim(), row.Barcode.Trim(), StringComparison.OrdinalIgnoreCase)) ||
                         (saved.Product?.Barcode != null &&
                          string.Equals(saved.Product.Barcode.Trim(), row.Barcode.Trim(), StringComparison.OrdinalIgnoreCase))) ||
                     string.Equals(
                         (saved.Product?.ProductName ?? saved.ProductName ?? string.Empty).Trim(),
                         row.ProductName.Trim(),
                         StringComparison.OrdinalIgnoreCase))));

            return new LowStockItemViewModel
            {
                ProductId = row.ProductId,
                BatchId = row.BatchId,
                CatalogId = row.CatalogId,
                ReorderListItemId = savedReorderItem?.ReorderListItemId,
                ProductName = row.ProductName,
                GenericName = row.GenericName,
                BrandName = row.BrandName,
                Barcode = row.Barcode,
                BatchNumber = row.BatchNumber,
                Quantity = row.Quantity,
                ExpiryDate = row.ExpiryDate,
                SellingUnitPrice = row.SellingUnitPrice,
                StockValue = row.Quantity * row.SellingUnitPrice
            };
        }).ToList();

        var reorderListCount = savedReorderItems.Count;

        return View(new LowStockViewModel
        {
            Count = items.Count,
            ReorderListCount = reorderListCount,
            Items = items
        });
    }
}
