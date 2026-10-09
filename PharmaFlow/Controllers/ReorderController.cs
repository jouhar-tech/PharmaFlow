using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PharmaFlow.Data;
using PharmaFlow.Filters;
using PharmaFlow.Models;
using PharmaFlow.Models.ViewModels;
using PharmaFlow.Services;

namespace PharmaFlow.Controllers;

[SessionAuthorize]
public sealed class ReorderController : Controller
{
    private readonly ApplicationDbContext _dbContext;
    private readonly IGlobalProductCatalogService _globalProductCatalogService;

    public ReorderController(
        ApplicationDbContext dbContext,
        IGlobalProductCatalogService globalProductCatalogService)
    {
        _dbContext = dbContext;
        _globalProductCatalogService = globalProductCatalogService;
    }

    [HttpGet]
    public async Task<IActionResult> Index(string? search, CancellationToken cancellationToken)
    {
        if (!TryGetProfileId(out var profileId))
            return RedirectToAction("Login", "Account");

        var searchTerm = search?.Trim() ?? string.Empty;
        if (searchTerm.Length > 100)
            searchTerm = searchTerm[..100];

        var savedItems = await _dbContext.ReorderListItems
            .AsNoTracking()
            .Include(item => item.Product)
            .Where(item => item.ProfileId == profileId)
            .ToListAsync(cancellationToken);

        // Preserve the previous behavior for inventory products that were marked inactive.
        var visibleSavedItems = savedItems
            .Where(item => !item.ProductId.HasValue || item.Product?.IsActive == true)
            .ToList();

        var catalogSavedItems = visibleSavedItems
            .Where(item => !item.ProductId.HasValue)
            .ToList();

        var matchingInventoryProducts = await FindInventoryMatchesAsync(
            profileId,
            catalogSavedItems.Select(item => new GlobalProductSearchResult
            {
                CatalogId = item.CatalogId,
                Source = item.Source ?? string.Empty,
                ExternalId = item.ExternalId ?? string.Empty,
                ProductName = item.ProductName ?? string.Empty,
                Barcode = item.Barcode
            }).ToList(),
            cancellationToken);

        var productIds = visibleSavedItems
            .Where(item => item.ProductId.HasValue)
            .Select(item => item.ProductId!.Value)
            .Concat(matchingInventoryProducts.Select(product => product.ProductId))
            .Distinct()
            .ToArray();

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

        // Inventory-backed entries clear when their usable stock increases above the saved baseline.
        var restockedInventoryItemIds = visibleSavedItems
            .Where(item =>
                item.ProductId.HasValue &&
                stockTotals.GetValueOrDefault(item.ProductId.Value) > item.QuantityWhenAdded)
            .Select(item => item.ReorderListItemId)
            .ToHashSet();

        // Catalog-only entries clear once a matching inventory product has usable stock.
        var restockedCatalogItemIds = catalogSavedItems
            .Where(item => matchingInventoryProducts.Any(product =>
                MatchesCatalogItem(item, product) &&
                stockTotals.GetValueOrDefault(product.ProductId) > 0m))
            .Select(item => item.ReorderListItemId)
            .ToHashSet();

        var autoRemovedIds = restockedInventoryItemIds
            .Concat(restockedCatalogItemIds)
            .Distinct()
            .ToArray();

        if (autoRemovedIds.Length > 0)
        {
            await _dbContext.ReorderListItems
                .Where(item => item.ProfileId == profileId && autoRemovedIds.Contains(item.ReorderListItemId))
                .ExecuteDeleteAsync(cancellationToken);
        }

        var remainingItems = visibleSavedItems
            .Where(item => !autoRemovedIds.Contains(item.ReorderListItemId))
            .OrderBy(item => item.Product?.ProductName ?? item.ProductName ?? string.Empty)
            .Select(item => new ReorderListProductViewModel
            {
                ReorderListItemId = item.ReorderListItemId,
                ProductId = item.ProductId,
                CatalogId = item.CatalogId ?? item.Product?.CatalogId,
                Source = item.Source,
                ExternalId = item.ExternalId,
                ProductName = item.Product?.ProductName ?? item.ProductName ?? "Unnamed product",
                GenericName = item.Product?.GenericName ?? item.GenericName,
                BrandName = item.Product?.BrandName ?? item.BrandName,
                Barcode = item.Product?.Barcode ?? item.Barcode,
                CurrentQuantity = item.ProductId.HasValue
                    ? stockTotals.GetValueOrDefault(item.ProductId.Value)
                    : 0m,
                QuantityWhenAdded = item.QuantityWhenAdded,
                AddedAt = item.CreatedAt
            })
            .ToList();

        var catalogProducts = new List<ReorderCatalogProductViewModel>();
        if (searchTerm.Length >= 2)
        {
            var results = (await _globalProductCatalogService.SearchAsync(
                searchTerm,
                cancellationToken)).ToList();

            if (results.Count > 0)
            {
                var inventoryMatches = await FindInventoryMatchesAsync(
                    profileId,
                    results,
                    cancellationToken);

                catalogProducts = results
                    .Where(result =>
                        !inventoryMatches.Any(inventory => MatchesCatalogResult(result, inventory)) &&
                        !remainingItems.Any(saved => MatchesCatalogResult(result, saved)))
                    .Select(ReorderCatalogProductViewModel.FromResult)
                    .OrderBy(item => item.ProductName)
                    .Take(100)
                    .ToList();
            }
        }

        return View(new ReorderListViewModel
        {
            SearchTerm = searchTerm,
            Count = remainingItems.Count,
            Items = remainingItems,
            CatalogProducts = catalogProducts
        });
    }

    // Existing low-stock Reorder button: save an item already in this pharmacy's inventory.
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
            .Where(item => item.ProductId == productId &&
                           item.ProfileId == profileId &&
                           item.IsActive)
            .Select(item => new
            {
                item.ProductId,
                item.CatalogId,
                item.ProductName,
                item.GenericName,
                item.BrandName,
                item.Barcode
            })
            .SingleOrDefaultAsync(cancellationToken);

        if (product is null)
            return NotFound();

        var alreadySaved = await _dbContext.ReorderListItems
            .AsNoTracking()
            .Include(item => item.Product)
            .Where(item => item.ProfileId == profileId)
            .AnyAsync(item =>
                item.ProductId == productId ||
                (item.ProductId == null &&
                    ((product.CatalogId.HasValue && item.CatalogId == product.CatalogId) ||
                     (!string.IsNullOrWhiteSpace(product.Barcode) &&
                        item.Barcode != null &&
                        item.Barcode.ToLower() == product.Barcode.ToLower()) ||
                     (item.ProductName != null &&
                        item.ProductName.ToLower() == product.ProductName.ToLower()))),
                cancellationToken);

        if (alreadySaved)
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
            CatalogId = product.CatalogId,
            ProductName = product.ProductName,
            GenericName = product.GenericName,
            BrandName = product.BrandName,
            Barcode = product.Barcode,
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
            _dbContext.Entry(_dbContext.ChangeTracker.Entries<ReorderListItem>()
                .Select(entry => entry.Entity)
                .First(item => item.ProductId == productId)).State = EntityState.Detached;

            var duplicateNowExists = await _dbContext.ReorderListItems
                .AsNoTracking()
                .AnyAsync(item => item.ProfileId == profileId && item.ProductId == productId, cancellationToken);

            if (!duplicateNowExists)
                throw;

            TempData["ReorderListError"] = "Product already added to Reorder List";
        }

        return RedirectToAction(nameof(Index));
    }

    // Save a global-catalog result without creating a local Product or stock batch.
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AddCatalog(
        string? source,
        string? externalId,
        long? catalogId,
        string? search,
        CancellationToken cancellationToken)
    {
        if (!TryGetProfileId(out var profileId))
            return RedirectToAction("Login", "Account");

        if (string.IsNullOrWhiteSpace(source) ||
            string.IsNullOrWhiteSpace(externalId) ||
            source.Length > 50 ||
            externalId.Length > 160)
        {
            TempData["ReorderListError"] = "Select a valid product from the Global Product Catalog.";
            return RedirectToAction(nameof(Index), new { search });
        }

        var result = await _globalProductCatalogService.GetAsync(
            source.Trim(),
            externalId.Trim(),
            catalogId,
            cancellationToken);

        if (result is null)
        {
            TempData["ReorderListError"] = "This catalog product is no longer available. Search again.";
            return RedirectToAction(nameof(Index), new { search });
        }

        if (await ProductExistsInInventoryAsync(profileId, result, cancellationToken))
        {
            TempData["ReorderListError"] = "This product is already in your inventory.";
            return RedirectToAction(nameof(Index), new { search = result.ProductName });
        }

        var duplicateExists = await ReorderEntryExistsAsync(profileId, result, cancellationToken);
        if (duplicateExists)
        {
            TempData["ReorderListError"] = "Product already added to Reorder List";
            return RedirectToAction(nameof(Index), new { search = result.ProductName });
        }

        var reorderItem = new ReorderListItem
        {
            ProfileId = profileId,
            ProductId = null,
            CatalogId = result.CatalogId,
            Source = result.Source,
            ExternalId = result.ExternalId,
            ProductName = result.ProductName,
            GenericName = result.GenericName,
            BrandName = result.BrandName,
            Barcode = result.Barcode,
            QuantityWhenAdded = 0m,
            CreatedAt = DateTime.UtcNow
        };

        _dbContext.ReorderListItems.Add(reorderItem);

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
            TempData["ReorderListSuccess"] = "Product added to Reorder List";
        }
        catch (DbUpdateException)
        {
            _dbContext.Entry(reorderItem).State = EntityState.Detached;

            var duplicateNowExists = await _dbContext.ReorderListItems
                .AsNoTracking()
                .AnyAsync(item =>
                    item.ProfileId == profileId &&
                    item.ProductId == null &&
                    item.Source == result.Source &&
                    item.ExternalId == result.ExternalId,
                    cancellationToken);

            if (!duplicateNowExists)
                throw;

            TempData["ReorderListError"] = "Product already added to Reorder List";
        }

        return RedirectToAction(nameof(Index), new { search = result.ProductName });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Remove(long reorderListItemId, CancellationToken cancellationToken)
    {
        if (!TryGetProfileId(out var profileId))
            return RedirectToAction("Login", "Account");

        var item = await _dbContext.ReorderListItems
            .FirstOrDefaultAsync(
                row => row.ProfileId == profileId && row.ReorderListItemId == reorderListItemId,
                cancellationToken);

        if (item is not null)
        {
            _dbContext.ReorderListItems.Remove(item);
            await _dbContext.SaveChangesAsync(cancellationToken);
        }

        return RedirectToAction(nameof(Index));
    }

    private async Task<bool> ProductExistsInInventoryAsync(
        long profileId,
        GlobalProductSearchResult result,
        CancellationToken cancellationToken)
    {
        var normalizedName = result.ProductName.Trim().ToLowerInvariant();
        var normalizedBarcode = result.Barcode?.Trim().ToLowerInvariant();

        return await _dbContext.Products
            .AsNoTracking()
            .AnyAsync(product =>
                product.ProfileId == profileId &&
                product.IsActive &&
                ((result.CatalogId.HasValue && product.CatalogId == result.CatalogId) ||
                 (normalizedBarcode != null &&
                    product.Barcode != null &&
                    product.Barcode.ToLower() == normalizedBarcode) ||
                 product.ProductName.ToLower() == normalizedName),
                cancellationToken);
    }

    private async Task<bool> ReorderEntryExistsAsync(
        long profileId,
        GlobalProductSearchResult result,
        CancellationToken cancellationToken)
    {
        var normalizedName = result.ProductName.Trim().ToLowerInvariant();
        var normalizedBarcode = result.Barcode?.Trim().ToLowerInvariant();
        var normalizedSource = result.Source.Trim().ToLowerInvariant();
        var normalizedExternalId = result.ExternalId.Trim();

        return await _dbContext.ReorderListItems
            .AsNoTracking()
            .AnyAsync(item =>
                item.ProfileId == profileId &&
                (
                    (item.ProductId == null &&
                        item.Source != null &&
                        item.ExternalId != null &&
                        item.Source.ToLower() == normalizedSource &&
                        item.ExternalId == normalizedExternalId) ||
                    (result.CatalogId.HasValue &&
                        (item.CatalogId == result.CatalogId ||
                         item.Product != null && item.Product.CatalogId == result.CatalogId)) ||
                    (normalizedBarcode != null &&
                        ((item.Barcode != null && item.Barcode.ToLower() == normalizedBarcode) ||
                         (item.Product != null && item.Product.Barcode != null &&
                          item.Product.Barcode.ToLower() == normalizedBarcode))) ||
                    (item.ProductName != null && item.ProductName.ToLower() == normalizedName) ||
                    (item.Product != null && item.Product.ProductName.ToLower() == normalizedName)
                ),
                cancellationToken);
    }

    private async Task<List<InventoryProductIdentity>> FindInventoryMatchesAsync(
        long profileId,
        IReadOnlyCollection<GlobalProductSearchResult> results,
        CancellationToken cancellationToken)
    {
        if (results.Count == 0)
            return [];

        var candidateCatalogIds = results
            .Where(item => item.CatalogId.HasValue)
            .Select(item => item.CatalogId!.Value)
            .Distinct()
            .ToArray();
        var candidateBarcodes = results
            .Where(item => !string.IsNullOrWhiteSpace(item.Barcode))
            .Select(item => item.Barcode!.Trim().ToLowerInvariant())
            .Distinct()
            .ToArray();
        var candidateNames = results
            .Select(item => item.ProductName.Trim().ToLowerInvariant())
            .Where(name => name.Length > 0)
            .Distinct()
            .ToArray();

        return await _dbContext.Products
            .AsNoTracking()
            .Where(product =>
                product.ProfileId == profileId &&
                product.IsActive &&
                ((candidateCatalogIds.Length > 0 &&
                    product.CatalogId.HasValue &&
                    candidateCatalogIds.Contains(product.CatalogId.Value)) ||
                 (candidateBarcodes.Length > 0 &&
                    product.Barcode != null &&
                    candidateBarcodes.Contains(product.Barcode.ToLower())) ||
                 (candidateNames.Length > 0 &&
                    candidateNames.Contains(product.ProductName.ToLower())))
            .Select(product => new InventoryProductIdentity
            {
                ProductId = product.ProductId,
                CatalogId = product.CatalogId,
                ProductName = product.ProductName,
                Barcode = product.Barcode
            })
            .ToListAsync(cancellationToken);
    }

    private static bool MatchesCatalogResult(
        GlobalProductSearchResult result,
        InventoryProductIdentity product) =>
        (result.CatalogId.HasValue && product.CatalogId == result.CatalogId) ||
        (!string.IsNullOrWhiteSpace(result.Barcode) &&
            !string.IsNullOrWhiteSpace(product.Barcode) &&
            string.Equals(result.Barcode.Trim(), product.Barcode.Trim(), StringComparison.OrdinalIgnoreCase)) ||
        string.Equals(result.ProductName.Trim(), product.ProductName.Trim(), StringComparison.OrdinalIgnoreCase);

    private static bool MatchesCatalogResult(
        GlobalProductSearchResult result,
        ReorderListProductViewModel item) =>
        (result.CatalogId.HasValue && item.CatalogId == result.CatalogId) ||
        (!string.IsNullOrWhiteSpace(result.Barcode) &&
            !string.IsNullOrWhiteSpace(item.Barcode) &&
            string.Equals(result.Barcode.Trim(), item.Barcode.Trim(), StringComparison.OrdinalIgnoreCase)) ||
        string.Equals(result.ProductName.Trim(), item.ProductName.Trim(), StringComparison.OrdinalIgnoreCase) ||
        (!item.ProductId.HasValue &&
            string.Equals(result.Source, item.Source, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(result.ExternalId, item.ExternalId, StringComparison.Ordinal));

    private static bool MatchesCatalogItem(
        ReorderListItem item,
        InventoryProductIdentity product) =>
        (item.CatalogId.HasValue && product.CatalogId == item.CatalogId) ||
        (!string.IsNullOrWhiteSpace(item.Barcode) &&
            !string.IsNullOrWhiteSpace(product.Barcode) &&
            string.Equals(item.Barcode.Trim(), product.Barcode.Trim(), StringComparison.OrdinalIgnoreCase)) ||
        (!string.IsNullOrWhiteSpace(item.ProductName) &&
            string.Equals(item.ProductName.Trim(), product.ProductName.Trim(), StringComparison.OrdinalIgnoreCase));

    private bool TryGetProfileId(out long profileId) =>
        long.TryParse(HttpContext.Session.GetString("ProfileId"), out profileId);

    private sealed class InventoryProductIdentity
    {
        public long ProductId { get; init; }
        public long? CatalogId { get; init; }
        public string ProductName { get; init; } = string.Empty;
        public string? Barcode { get; init; }
    }
}
