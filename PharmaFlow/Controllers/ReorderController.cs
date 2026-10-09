using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PharmaFlow.Data;
using PharmaFlow.Filters;
using PharmaFlow.Models;
using PharmaFlow.Models.ViewModels;

namespace PharmaFlow.Controllers;

[SessionAuthorize]
public sealed class ReorderController : Controller
{
    private readonly ApplicationDbContext _dbContext;

    public ReorderController(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    [HttpGet]
    public async Task<IActionResult> Index(string? search, CancellationToken cancellationToken)
    {
        if (!TryGetProfileId(out var profileId))
            return RedirectToAction("Login", "Account");

        var savedItems = await _dbContext.ReorderListItems
            .AsNoTracking()
            .Where(item => item.ProfileId == profileId && item.Product.IsActive)
            .Select(item => new
            {
                item.ReorderListItemId,
                item.ProductId,
                item.QuantityWhenAdded,
                item.CreatedAt,
                item.Product.ProductName,
                item.Product.GenericName,
                item.Product.BrandName,
                item.Product.Barcode
            })
            .ToListAsync(cancellationToken);

        var productIds = savedItems.Select(item => item.ProductId).Distinct().ToArray();
        var stockTotals = productIds.Length == 0
            ? new Dictionary<long, decimal>()
            : await _dbContext.ProductBatches
                .AsNoTracking()
                .Where(batch =>
                    productIds.Contains(batch.ProductId) &&
                    batch.IsActive &&
                    !batch.IsQuarantined &&
                    batch.QuantityOnHand > 0)
                .GroupBy(batch => batch.ProductId)
                .Select(group => new
                {
                    ProductId = group.Key,
                    Quantity = group.Sum(batch => batch.QuantityOnHand)
                })
                .ToDictionaryAsync(row => row.ProductId, row => row.Quantity, cancellationToken);

        var restockedIds = savedItems
            .Where(item => stockTotals.GetValueOrDefault(item.ProductId) > item.QuantityWhenAdded)
            .Select(item => item.ReorderListItemId)
            .ToArray();

        if (restockedIds.Length > 0)
        {
            await _dbContext.ReorderListItems
                .Where(item => item.ProfileId == profileId && restockedIds.Contains(item.ReorderListItemId))
                .ExecuteDeleteAsync(cancellationToken);
        }

        var remaining = savedItems
            .Where(item => !restockedIds.Contains(item.ReorderListItemId))
            .Select(item => new ReorderListProductViewModel
            {
                ProductId = item.ProductId,
                ProductName = item.ProductName,
                GenericName = item.GenericName,
                BrandName = item.BrandName,
                Barcode = item.Barcode,
                CurrentQuantity = stockTotals.GetValueOrDefault(item.ProductId),
                QuantityWhenAdded = item.QuantityWhenAdded,
                AddedAt = item.CreatedAt
            })
            .Where(item => string.IsNullOrWhiteSpace(search) ||
                item.ProductName.Contains(search.Trim(), StringComparison.OrdinalIgnoreCase) ||
                (item.GenericName?.Contains(search.Trim(), StringComparison.OrdinalIgnoreCase) ?? false) ||
                (item.BrandName?.Contains(search.Trim(), StringComparison.OrdinalIgnoreCase) ?? false) ||
                (item.Barcode?.Contains(search.Trim(), StringComparison.OrdinalIgnoreCase) ?? false))
            .OrderBy(item => item.ProductName)
            .ToList();

        return View(new ReorderListViewModel
        {
            SearchTerm = search?.Trim() ?? string.Empty,
            Count = remaining.Count,
            Items = remaining
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Add(long productId, CancellationToken cancellationToken)
    {
        if (!TryGetProfileId(out var profileId))
            return RedirectToAction("Login", "Account");

        if (productId <= 0)
            return NotFound();

        var product = await _dbContext.Products
            .AsNoTracking()
            .Where(item => item.ProductId == productId && item.ProfileId == profileId && item.IsActive)
            .Select(item => new { item.ProductId })
            .SingleOrDefaultAsync(cancellationToken);

        if (product is null)
            return NotFound();

        var existing = await _dbContext.ReorderListItems
            .AsNoTracking()
            .AnyAsync(item => item.ProfileId == profileId && item.ProductId == productId, cancellationToken);

        if (existing)
        {
            TempData["ReorderListError"] = "Product already added to Reorder List";
            return RedirectToAction(nameof(Index));
        }

        var currentQuantity = await _dbContext.ProductBatches
            .AsNoTracking()
            .Where(batch =>
                batch.ProductId == productId &&
                batch.IsActive &&
                !batch.IsQuarantined &&
                batch.QuantityOnHand > 0)
            .SumAsync(batch => (decimal?)batch.QuantityOnHand, cancellationToken) ?? 0m;

        _dbContext.ReorderListItems.Add(new ReorderListItem
        {
            ProfileId = profileId,
            ProductId = productId,
            QuantityWhenAdded = currentQuantity,
            CreatedAt = DateTime.UtcNow
        });

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
            TempData["ReorderListSuccess"] = "Product added to Reorder List";
        }
        catch (DbUpdateException)
        {
            // The unique profile/product index also protects against duplicate
            // submissions arriving at nearly the same time.
            var duplicateNowExists = await _dbContext.ReorderListItems
                .AsNoTracking()
                .AnyAsync(item => item.ProfileId == profileId && item.ProductId == productId, cancellationToken);

            if (!duplicateNowExists)
                throw;

            TempData["ReorderListError"] = "Product already added to Reorder List";
        }

        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Remove(long productId, CancellationToken cancellationToken)
    {
        if (!TryGetProfileId(out var profileId))
            return RedirectToAction("Login", "Account");

        var item = await _dbContext.ReorderListItems
            .FirstOrDefaultAsync(
                row => row.ProfileId == profileId && row.ProductId == productId,
                cancellationToken);

        if (item is not null)
        {
            _dbContext.ReorderListItems.Remove(item);
            await _dbContext.SaveChangesAsync(cancellationToken);
        }

        return RedirectToAction(nameof(Index));
    }

    private bool TryGetProfileId(out long profileId) =>
        long.TryParse(HttpContext.Session.GetString("ProfileId"), out profileId);
}
