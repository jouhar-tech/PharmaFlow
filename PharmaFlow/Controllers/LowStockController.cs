using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Npgsql.EntityFrameworkCore.PostgreSQL;
using PharmaFlow.Data;
using PharmaFlow.Filters;
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

        var items = rows.Select(row => new LowStockItemViewModel
        {
            ProductId = row.ProductId,
            BatchId = row.BatchId,
            ProductName = row.ProductName,
            GenericName = row.GenericName,
            BrandName = row.BrandName,
            Barcode = row.Barcode,
            BatchNumber = row.BatchNumber,
            Quantity = row.Quantity,
            ExpiryDate = row.ExpiryDate,
            SellingUnitPrice = row.SellingUnitPrice,
            StockValue = row.Quantity * row.SellingUnitPrice
        }).ToList();

        return View(new LowStockViewModel
        {
            Count = items.Count,
            Items = items
        });
    }
}
