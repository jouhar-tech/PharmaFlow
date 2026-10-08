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
            () => _dbContext.Products
                .AsNoTracking()
                .Where(product =>
                    product.ProfileId == profileId &&
                    product.IsActive)
                .SelectMany(
                    product => product.Batches.Where(batch =>
                        batch.IsActive &&
                        !batch.IsQuarantined &&
                        batch.QuantityOnHand > 0 &&
                        batch.QuantityOnHand < 3),
                    (product, batch) => new
                    {
                        ProductId = product.ProductId,
                        BatchId = batch.BatchId,
                        ProductName = product.ProductName,
                        GenericName = product.GenericName,
                        BrandName = product.BrandName,
                        Barcode = product.Barcode,
                        BatchNumber = batch.BatchNumber,
                        Quantity = batch.QuantityOnHand,
                        ExpiryDate = batch.ExpiryDate,
                        SellingUnitPrice = batch.SellingUnitPrice
                    })
                .OrderBy(row => row.ProductName)
                .ThenBy(row => row.Quantity)
                .ThenBy(row => row.ExpiryDate)
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
