using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PharmaFlow.Data;
using PharmaFlow.Filters;
using PharmaFlow.Models;
using PharmaFlow.Models.ViewModels;
using PharmaFlow.Services;

namespace PharmaFlow.Controllers;

[SessionAuthorize]
public sealed class InventoryController : Controller
{
    private readonly ApplicationDbContext _dbContext;
    private readonly IGlobalProductCatalogService _globalProductCatalogService;
    private readonly ILogger<InventoryController> _logger;

    public InventoryController(
        ApplicationDbContext dbContext,
        IGlobalProductCatalogService globalProductCatalogService,
        ILogger<InventoryController> logger)
    {
        _dbContext = dbContext;
        _globalProductCatalogService = globalProductCatalogService;
        _logger = logger;
    }

    [HttpGet]
    public async Task<IActionResult> SearchProducts(
        string? query,
        CancellationToken cancellationToken)
    {
        if (!TryGetProfileId(out var profileId))
            return RedirectToAction("Login", "Account");

        var normalizedQuery = NormalizeNullableText(query, 100) ?? string.Empty;
        var results = normalizedQuery.Length >= 2
            ? (await _globalProductCatalogService.SearchAsync(normalizedQuery, cancellationToken)).ToList()
            : [];

        if (results.Count > 0)
        {
            var candidateCatalogIds = results
                .Where(item => item.CatalogId.HasValue)
                .Select(item => item.CatalogId!.Value)
                .Distinct()
                .ToArray();

            var candidateBarcodes = results
                .Where(item => !string.IsNullOrWhiteSpace(item.Barcode))
                .Select(item => item.Barcode!.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            var candidateNames = results
                .Select(item => item.ProductName.Trim().ToLower())
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Distinct()
                .ToArray();

            // Only active products count as already present in the current inventory.
            // Inactive products were previously removed from stock and should remain
            // discoverable so the owner can add stock again later.
            var existingProducts = await _dbContext.Products
                .AsNoTracking()
                .Where(p =>
                    p.ProfileId == profileId &&
                    p.IsActive &&
                    (
                        (p.CatalogId.HasValue && candidateCatalogIds.Contains(p.CatalogId.Value))
                        || (p.Barcode != null && candidateBarcodes.Contains(p.Barcode))
                        || candidateNames.Contains(p.ProductName.ToLower())
                    ))
                .Select(p => new { p.CatalogId, p.Barcode, p.ProductName })
                .ToListAsync(cancellationToken);

            var existingCatalogIds = existingProducts
                .Where(item => item.CatalogId.HasValue)
                .Select(item => item.CatalogId!.Value)
                .ToHashSet();

            var existingBarcodes = existingProducts
                .Where(item => !string.IsNullOrWhiteSpace(item.Barcode))
                .Select(item => item.Barcode!.Trim())
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            var existingNames = existingProducts
                .Select(item => item.ProductName.Trim())
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            results = results
                .Where(item =>
                    !(item.CatalogId.HasValue && existingCatalogIds.Contains(item.CatalogId.Value)) &&
                    !(item.Barcode != null && existingBarcodes.Contains(item.Barcode.Trim())) &&
                    !existingNames.Contains(item.ProductName.Trim()))
                .ToList();
        }

        ViewData["Title"] = "Search Products";
        return View(new InventoryProductSearchViewModel
        {
            Query = normalizedQuery,
            Products = results
                .Select(item => new InventoryProductSearchItemViewModel
                {
                    CatalogId = item.CatalogId,
                    Source = item.Source,
                    ExternalId = item.ExternalId,
                    ProductType = item.ProductType,
                    ProductName = item.ProductName,
                    GenericName = item.GenericName,
                    BrandName = item.BrandName,
                    Manufacturer = item.Manufacturer,
                    DosageForm = item.DosageForm,
                    Strength = item.Strength,
                    PackSize = item.PackSize,
                    Barcode = item.Barcode
                })
                .ToList()
        });
    }

    [HttpGet]
    public IActionResult BarcodeScanner()
    {
        return View();
    }

    [HttpGet]
    public async Task<IActionResult> AddItem(
        long? productId,
        long? catalogId,
        string? source,
        string? externalId,
        CancellationToken cancellationToken)
    {
        if (!TryGetProfileId(out var profileId))
            return RedirectToAction("Login", "Account");

        if (productId.HasValue &&
            (catalogId.HasValue ||
             !string.IsNullOrWhiteSpace(source) ||
             !string.IsNullOrWhiteSpace(externalId)))
            return BadRequest();

        if (catalogId.HasValue &&
            (string.IsNullOrWhiteSpace(source) ||
             string.IsNullOrWhiteSpace(externalId)))
            return BadRequest();

        if (!catalogId.HasValue &&
            string.IsNullOrWhiteSpace(source) != string.IsNullOrWhiteSpace(externalId))
            return BadRequest();

        var model = new InventoryAddItemViewModel
        {
            ExistingProductId = productId,
            CatalogId = catalogId,
            ExternalSource = NormalizeNullableText(source, 50),
            ExternalId = NormalizeNullableText(externalId, 160),
            ExpiryDate = GetIndiaToday().AddYears(2)
        };

        if (productId.HasValue)
        {
            var product = await _dbContext.Products
                .AsNoTracking()
                .Where(p => p.ProductId == productId.Value && p.ProfileId == profileId)
                .Select(p => new
                {
                    p.ProductId,
                    p.ProductName,
                    LatestMrp = p.Batches
                        .OrderByDescending(b => b.CreatedAt)
                        .Select(b => (decimal?)b.SellingUnitPrice)
                        .FirstOrDefault()
                })
                .SingleOrDefaultAsync(cancellationToken);

            if (product is null)
                return NotFound();

            model.ProductName = product.ProductName;
            model.Mrp = product.LatestMrp.GetValueOrDefault();
        }
        else if (catalogId.HasValue || (!string.IsNullOrWhiteSpace(model.ExternalSource) && !string.IsNullOrWhiteSpace(model.ExternalId)))
        {
            var globalProduct = await _globalProductCatalogService.GetAsync(
                model.ExternalSource ?? string.Empty,
                model.ExternalId ?? string.Empty,
                model.CatalogId,
                cancellationToken);

            if (globalProduct is null)
                return NotFound();

            if (globalProduct.CatalogId.HasValue)
            {
                var alreadyInInventory = await _dbContext.Products
                    .AsNoTracking()
                    .AnyAsync(
                        p => p.ProfileId == profileId &&
                             p.CatalogId == globalProduct.CatalogId,
                        cancellationToken);

                if (alreadyInInventory)
                {
                    TempData["InventoryMessage"] = "This product is already linked to your inventory.";
                    return RedirectToAction(nameof(Index));
                }
            }
            else if (!string.IsNullOrWhiteSpace(globalProduct.Barcode))
            {
                var alreadyInInventory = await _dbContext.Products
                    .AsNoTracking()
                    .AnyAsync(
                        p => p.ProfileId == profileId &&
                             p.Barcode == globalProduct.Barcode,
                        cancellationToken);

                if (alreadyInInventory)
                {
                    TempData["InventoryMessage"] = "This barcode is already present in your inventory.";
                    return RedirectToAction(nameof(Index));
                }
            }

            model.CatalogId = globalProduct.CatalogId;
            model.ExternalSource = globalProduct.Source;
            model.ExternalId = globalProduct.ExternalId;
            model.ProductName = globalProduct.ProductName;
        }

        ViewData["Title"] = "Add Item";
        return View(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AddItem(
        InventoryAddItemViewModel model,
        CancellationToken cancellationToken)
    {
        if (!TryGetProfileId(out var profileId))
            return Unauthorized();

        if (model.ExistingProductId.HasValue && (model.CatalogId.HasValue || !string.IsNullOrWhiteSpace(model.ExternalSource) || !string.IsNullOrWhiteSpace(model.ExternalId)))
            return BadRequest();

        var hasGlobalSelection =
            model.CatalogId.HasValue ||
            !string.IsNullOrWhiteSpace(model.ExternalSource) ||
            !string.IsNullOrWhiteSpace(model.ExternalId);

        if (hasGlobalSelection &&
            (!string.IsNullOrWhiteSpace(model.ExternalSource) != !string.IsNullOrWhiteSpace(model.ExternalId)))
            return BadRequest();

        GlobalProductSearchResult? globalProduct = null;

        if (hasGlobalSelection)
        {
            if (string.IsNullOrWhiteSpace(model.ExternalSource) ||
                string.IsNullOrWhiteSpace(model.ExternalId))
                return BadRequest();

            globalProduct = await _globalProductCatalogService.GetAsync(
                model.ExternalSource,
                model.ExternalId,
                model.CatalogId,
                cancellationToken);

            if (globalProduct is null)
                return NotFound();

            model.ProductName = globalProduct.ProductName;
        }
        else
        {
            model.ProductName = NormalizeText(model.ProductName, 200) ?? string.Empty;
        }

        if (model.ExpiryDate == default)
            model.ExpiryDate = GetIndiaToday().AddYears(2);

        var today = GetIndiaToday();

        if (model.ExpiryDate < today)
            ModelState.AddModelError(nameof(model.ExpiryDate), "Expiry cannot be earlier than today.");

        if (model.Quantity <= 0 || model.Quantity > 999_999_999m)
            ModelState.AddModelError(nameof(model.Quantity), "Enter a valid quantity.");

        if (model.Mrp <= 0 || model.Mrp > 999_999_999m)
            ModelState.AddModelError(nameof(model.Mrp), "Enter a valid MRP.");

        if (!ModelState.IsValid)
            return View(model);

        await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);

        try
        {
            Product? product;

        if (model.ExistingProductId.HasValue)
        {
            product = await _dbContext.Products
                .FirstOrDefaultAsync(
                    p => p.ProductId == model.ExistingProductId.Value &&
                         p.ProfileId == profileId,
                    cancellationToken);

            if (product is null)
                return NotFound();

            product.IsActive = true;
            product.UpdatedAt = DateTime.UtcNow;
        }
        else if (globalProduct is not null)
        {
            if (globalProduct.CatalogId.HasValue)
            {
                product = await _dbContext.Products
                    .FirstOrDefaultAsync(
                        p => p.ProfileId == profileId &&
                             p.CatalogId == globalProduct.CatalogId,
                        cancellationToken);
            }
            else if (!string.IsNullOrWhiteSpace(globalProduct.Barcode))
            {
                product = await _dbContext.Products
                    .FirstOrDefaultAsync(
                        p => p.ProfileId == profileId &&
                             p.Barcode == globalProduct.Barcode,
                        cancellationToken);
            }
            else
            {
                product = await _dbContext.Products
                    .FirstOrDefaultAsync(
                        p => p.ProfileId == profileId &&
                             p.ProductName.ToLower() == globalProduct.ProductName.ToLower(),
                        cancellationToken);
            }

            if (product is null)
            {
                product = new Product
                {
                    ProfileId = profileId,
                    CatalogId = globalProduct.CatalogId,
                    ProductName = globalProduct.ProductName,
                    GenericName = globalProduct.GenericName,
                    BrandName = globalProduct.BrandName,
                    DosageForm = globalProduct.DosageForm,
                    Strength = globalProduct.Strength,
                    PackSize = globalProduct.PackSize,
                    Barcode = globalProduct.Barcode,
                    Manufacturer = globalProduct.Manufacturer,
                    HsnCode = globalProduct.HsnCode,
                    GstRate = globalProduct.GstRate,
                    IsPrescriptionRequired = globalProduct.IsPrescriptionRequired,
                    IsActive = true,
                    ReorderLevel = 0,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                };

                _dbContext.Products.Add(product);
                await _dbContext.SaveChangesAsync(cancellationToken);
            }
            else
            {
                product.IsActive = true;
                product.UpdatedAt = DateTime.UtcNow;
            }
        }
        else
        {
            product = await _dbContext.Products
                .FirstOrDefaultAsync(
                    p => p.ProfileId == profileId &&
                         p.ProductName.ToLower() == model.ProductName.ToLower(),
                    cancellationToken);

            if (product is null)
            {
                product = new Product
                {
                    ProfileId = profileId,
                    ProductName = model.ProductName,
                    IsActive = true,
                    ReorderLevel = 0,
                    IsPrescriptionRequired = false,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                };

                _dbContext.Products.Add(product);
                await _dbContext.SaveChangesAsync(cancellationToken);
            }
            else
            {
                product.IsActive = true;
                product.UpdatedAt = DateTime.UtcNow;
            }
        }

        var batchNumber = $"MANUAL-{DateTime.UtcNow:yyyyMMddHHmmss}-{Guid.NewGuid():N}"[..26];

        var batch = new ProductBatch
        {
            ProductId = product.ProductId,
            BatchNumber = batchNumber,
            ExpiryDate = model.ExpiryDate,
            QuantityOnHand = decimal.Round(model.Quantity, 2, MidpointRounding.AwayFromZero),
            PurchaseUnitPrice = 0m,
            SellingUnitPrice = decimal.Round(model.Mrp, 2, MidpointRounding.AwayFromZero),
            IsQuarantined = false,
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        _dbContext.ProductBatches.Add(batch);
        await _dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        TempData["InventoryMessage"] = "Item added to inventory successfully.";
        return RedirectToAction(nameof(Index));
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync(CancellationToken.None);
            _logger.LogError(ex, "Failed to add inventory item for profile {ProfileId}.", profileId);
            ModelState.AddModelError(
                string.Empty,
                "The item could not be added. No inventory changes were saved.");
            return View(model);
        }
    }

    [HttpGet]
    public async Task<IActionResult> FindByBarcode(
        string? barcode,
        CancellationToken cancellationToken)
    {
        if (!TryGetProfileId(out var profileId))
            return Unauthorized();

        var normalizedBarcode = NormalizeNullableText(barcode, 100);
        if (normalizedBarcode is null)
            return BadRequest(new { message = "Barcode is required." });

        var product = await _dbContext.Products
            .AsNoTracking()
            .Where(p => p.ProfileId == profileId && p.Barcode != null && p.Barcode == normalizedBarcode)
            .Select(p => new
            {
                p.ProductId,
                p.ProductName
            })
            .SingleOrDefaultAsync(cancellationToken);

        return product is null
            ? NotFound(new { message = "No matching product was found for this barcode." })
            : Ok(product);
    }

    [HttpGet]
    public async Task<IActionResult> Details(
        long productId,
        long? batchId,
        string? returnTo,
        CancellationToken cancellationToken)
    {
        if (!TryGetProfileId(out var profileId))
            return RedirectToAction("Login", "Account");

        if (productId <= 0)
            return NotFound();

        var product = await _dbContext.Products
            .AsNoTracking()
            .Where(p => p.ProductId == productId && p.ProfileId == profileId && p.IsActive)
            .SingleOrDefaultAsync(cancellationToken);

        if (product is null)
            return NotFound();

        var batches = await _dbContext.ProductBatches
            .AsNoTracking()
            .Where(b => b.ProductId == productId)
            .OrderByDescending(b => b.IsActive)
            .ThenBy(b => b.ExpiryDate)
            .Select(b => new InventoryBatchDetailsViewModel
            {
                BatchId = b.BatchId,
                BatchNumber = b.BatchNumber,
                ManufacturingDate = b.ManufacturingDate,
                ExpiryDate = b.ExpiryDate,
                QuantityOnHand = b.QuantityOnHand,
                PurchaseUnitPrice = b.PurchaseUnitPrice,
                SellingUnitPrice = b.SellingUnitPrice,
                SupplierId = b.SupplierId,
                Location = b.Location,
                IsQuarantined = b.IsQuarantined,
                IsActive = b.IsActive
            })
            .ToListAsync(cancellationToken);

        var selectedBatch = batchId.HasValue
            ? batches.FirstOrDefault(b => b.BatchId == batchId.Value)
            : batches.FirstOrDefault(b => b.IsActive && !b.IsQuarantined)
                ?? batches.FirstOrDefault(b => b.IsActive)
                ?? batches.FirstOrDefault();

        var model = new InventoryProductDetailsViewModel
        {
            ProductId = product.ProductId,
            ProductName = product.ProductName,
            GenericName = product.GenericName,
            BrandName = product.BrandName,
            DosageForm = product.DosageForm,
            Strength = product.Strength,
            PackSize = product.PackSize,
            Barcode = product.Barcode,
            Manufacturer = product.Manufacturer,
            HsnCode = product.HsnCode,
            GstRate = product.GstRate,
            ReorderLevel = product.ReorderLevel,
            IsPrescriptionRequired = product.IsPrescriptionRequired,
            IsActive = product.IsActive,
            IsBatchContext = batchId.HasValue,
            ReturnTo = NormalizeReturnTo(returnTo),
            SelectedBatch = selectedBatch,
            Batches = batches
        };

        ViewData["Title"] = "Product Details";
        return View(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateDetails(
        long productId,
        long batchId,
        string? productName,
        string? batchNumber,
        decimal quantityOnHand,
        decimal purchaseUnitPrice,
        decimal sellingUnitPrice,
        DateOnly expiryDate,
        string? manufacturer,
        decimal? gstRate,
        string? barcode,
        string? returnTo,
        CancellationToken cancellationToken)
    {
        if (!TryGetProfileId(out var profileId))
            return Unauthorized();

        if (productId <= 0 || batchId <= 0)
            return NotFound();

        var product = await _dbContext.Products
            .FirstOrDefaultAsync(
                p => p.ProductId == productId &&
                     p.ProfileId == profileId &&
                     p.IsActive,
                cancellationToken);

        if (product is null)
            return NotFound();

        var batch = await _dbContext.ProductBatches
            .FirstOrDefaultAsync(
                b => b.BatchId == batchId &&
                     b.ProductId == productId &&
                     b.Product.IsActive &&
                     b.Product.ProfileId == profileId,
                cancellationToken);

        if (batch is null)
            return NotFound();

        var normalizedName = NormalizeText(productName, 200);
        var normalizedBatch = NormalizeText(batchNumber, 100);
        var normalizedManufacturer = NormalizeNullableText(manufacturer, 200);
        var normalizedBarcode = NormalizeNullableText(barcode, 100);

        if (normalizedName is null)
            ModelState.AddModelError(nameof(productName), "Product name is required.");

        if (normalizedBatch is null)
            ModelState.AddModelError(nameof(batchNumber), "Batch number is required.");

        if (gstRate is < 0m or > 100m)
            ModelState.AddModelError(nameof(gstRate), "GST rate must be between 0 and 100.");

        if (quantityOnHand < 0m || quantityOnHand > 999_999_999m)
            ModelState.AddModelError(nameof(quantityOnHand), "Quantity is invalid.");

        if (purchaseUnitPrice < 0m || purchaseUnitPrice > 999_999_999m ||
            sellingUnitPrice < 0m || sellingUnitPrice > 999_999_999m)
            ModelState.AddModelError(nameof(purchaseUnitPrice), "Price is invalid.");

        if (!ModelState.IsValid)
        {
            TempData["InventoryDetailsError"] = "Please correct the highlighted details.";
            return RedirectToAction(nameof(Details), new { productId, batchId, returnTo = NormalizeReturnTo(returnTo) });
        }

        var duplicateBatch = await _dbContext.ProductBatches
            .AsNoTracking()
            .AnyAsync(
                b => b.ProductId == productId &&
                     b.BatchId != batchId &&
                     b.BatchNumber.ToLower() == normalizedBatch!.ToLower(),
                cancellationToken);

        if (duplicateBatch)
        {
            TempData["InventoryDetailsError"] = "Another batch with this batch number already exists for this product.";
            return RedirectToAction(nameof(Details), new { productId, batchId, returnTo = NormalizeReturnTo(returnTo) });
        }

        product.ProductName = normalizedName!;
        product.Manufacturer = normalizedManufacturer;
        product.GstRate = gstRate;
        product.Barcode = normalizedBarcode;
        product.UpdatedAt = DateTime.UtcNow;

        batch.BatchNumber = normalizedBatch!;
        batch.QuantityOnHand = decimal.Round(quantityOnHand, 2, MidpointRounding.AwayFromZero);
        batch.PurchaseUnitPrice = decimal.Round(purchaseUnitPrice, 2, MidpointRounding.AwayFromZero);
        batch.SellingUnitPrice = decimal.Round(sellingUnitPrice, 2, MidpointRounding.AwayFromZero);
        batch.ExpiryDate = expiryDate;
        batch.UpdatedAt = DateTime.UtcNow;

        await _dbContext.SaveChangesAsync(cancellationToken);

        TempData["InventoryDetailsSuccess"] = "Product details updated successfully.";
        return RedirectToAction(
            nameof(Details),
            new { productId, batchId, returnTo = NormalizeReturnTo(returnTo) });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateProduct(
        long productId,
        string? productName,
        string? genericName,
        string? brandName,
        string? dosageForm,
        string? strength,
        string? packSize,
        string? barcode,
        string? manufacturer,
        string? hsnCode,
        decimal? gstRate,
        decimal reorderLevel,
        bool isPrescriptionRequired,
        CancellationToken cancellationToken)
    {
        if (!TryGetProfileId(out var profileId))
            return Unauthorized();

        var product = await _dbContext.Products
            .FirstOrDefaultAsync(
                p => p.ProductId == productId &&
                     p.ProfileId == profileId &&
                     p.IsActive,
                cancellationToken);

        if (product is null)
            return NotFound();

        var normalizedName = NormalizeText(productName, 200);
        if (normalizedName is null)
            return BadRequest(new { message = "Product name is required." });

        if (gstRate is < 0m or > 100m)
            return BadRequest(new { message = "GST rate must be between 0 and 100." });

        if (reorderLevel < 0m || reorderLevel > 999_999m)
            return BadRequest(new { message = "Reorder level is invalid." });

        product.ProductName = normalizedName;
        product.GenericName = NormalizeNullableText(genericName, 200);
        product.BrandName = NormalizeNullableText(brandName, 200);
        product.DosageForm = NormalizeNullableText(dosageForm, 100);
        product.Strength = NormalizeNullableText(strength, 100);
        product.PackSize = NormalizeNullableText(packSize, 100);
        product.Barcode = NormalizeNullableText(barcode, 100);
        product.Manufacturer = NormalizeNullableText(manufacturer, 200);
        product.HsnCode = NormalizeNullableText(hsnCode, 20);
        product.GstRate = gstRate;
        product.ReorderLevel = decimal.Round(reorderLevel, 2, MidpointRounding.AwayFromZero);
        product.IsPrescriptionRequired = isPrescriptionRequired;
        product.UpdatedAt = DateTime.UtcNow;

        await _dbContext.SaveChangesAsync(cancellationToken);

        TempData["InventoryDetailsSuccess"] = "Product details updated successfully.";
        return RedirectToAction(nameof(Details), new { productId });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateBatch(
        long productId,
        long batchId,
        string? batchNumber,
        DateOnly expiryDate,
        DateOnly? manufacturingDate,
        decimal quantityOnHand,
        decimal purchaseUnitPrice,
        decimal sellingUnitPrice,
        long? supplierId,
        string? location,
        bool isQuarantined,
        CancellationToken cancellationToken)
    {
        if (!TryGetProfileId(out var profileId))
            return Unauthorized();

        var batch = await _dbContext.ProductBatches
            .FirstOrDefaultAsync(
                b => b.BatchId == batchId &&
                     b.ProductId == productId &&
                     b.Product.ProfileId == profileId &&
                     b.Product.IsActive,
                cancellationToken);

        if (batch is null)
            return NotFound();

        var normalizedBatch = NormalizeText(batchNumber, 100);
        if (normalizedBatch is null)
            return BadRequest(new { message = "Batch number is required." });

        if (manufacturingDate.HasValue && manufacturingDate.Value > expiryDate)
            return BadRequest(new { message = "Manufacturing date cannot be later than expiry date." });

        if (quantityOnHand < 0m || quantityOnHand > 999_999_999m)
            return BadRequest(new { message = "Quantity is invalid." });

        if (purchaseUnitPrice < 0m || purchaseUnitPrice > 999_999_999m ||
            sellingUnitPrice < 0m || sellingUnitPrice > 999_999_999m)
            return BadRequest(new { message = "Price is invalid." });

        var duplicateBatch = await _dbContext.ProductBatches
            .AsNoTracking()
            .AnyAsync(
                b => b.ProductId == productId &&
                     b.BatchId != batchId &&
                     b.BatchNumber.ToLower() == normalizedBatch.ToLower(),
                cancellationToken);

        if (duplicateBatch)
            return BadRequest(new { message = "Another batch with this batch number already exists for this product." });

        batch.BatchNumber = normalizedBatch;
        batch.ExpiryDate = expiryDate;
        batch.ManufacturingDate = manufacturingDate;
        batch.QuantityOnHand = decimal.Round(quantityOnHand, 2, MidpointRounding.AwayFromZero);
        batch.PurchaseUnitPrice = decimal.Round(purchaseUnitPrice, 2, MidpointRounding.AwayFromZero);
        batch.SellingUnitPrice = decimal.Round(sellingUnitPrice, 2, MidpointRounding.AwayFromZero);
        batch.SupplierId = supplierId;
        batch.Location = NormalizeNullableText(location, 120);
        batch.IsQuarantined = isQuarantined;
        batch.UpdatedAt = DateTime.UtcNow;

        await _dbContext.SaveChangesAsync(cancellationToken);

        TempData["InventoryDetailsSuccess"] = "Batch details updated successfully.";
        return RedirectToAction(nameof(Details), new { productId, batchId });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RemoveProduct(
        long productId,
        long? batchId,
        CancellationToken cancellationToken)
    {
        if (!TryGetProfileId(out var profileId))
            return Unauthorized();

        var product = await _dbContext.Products
            .FirstOrDefaultAsync(
                p => p.ProductId == productId &&
                     p.ProfileId == profileId &&
                     p.IsActive,
                cancellationToken);

        if (product is null)
            return NotFound();

        var now = DateTime.UtcNow;

        await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);

        try
        {
            if (batchId.HasValue)
            {
                var batch = await _dbContext.ProductBatches
                    .FirstOrDefaultAsync(
                        b => b.BatchId == batchId.Value &&
                             b.ProductId == productId &&
                             b.Product.ProfileId == profileId &&
                             b.IsActive,
                        cancellationToken);

                if (batch is null)
                    return NotFound();

                // Removing stock from an inventory/expiry/low-stock card is batch-specific.
                // Clear the quantity as well as IsActive so the returned stock cannot
                // accidentally reappear if the batch is reactivated later.
                batch.QuantityOnHand = 0m;
                batch.IsActive = false;
                batch.UpdatedAt = now;

                await _dbContext.SaveChangesAsync(cancellationToken);

                var hasUsableStock = await _dbContext.ProductBatches
                    .AsNoTracking()
                    .AnyAsync(
                        b => b.ProductId == productId &&
                             b.IsActive &&
                             !b.IsQuarantined &&
                             b.QuantityOnHand > 0,
                        cancellationToken);

                if (!hasUsableStock)
                {
                    product.IsActive = false;
                    product.UpdatedAt = now;
                    await _dbContext.SaveChangesAsync(cancellationToken);
                }

                await transaction.CommitAsync(cancellationToken);

                TempData["InventoryMessage"] = "Batch removed from stock successfully.";
                return RedirectToInventorySource(returnTo);
            }

            // No batch was supplied: keep the existing product-level removal behaviour.
            product.IsActive = false;
            product.UpdatedAt = now;

            await _dbContext.ProductBatches
                .Where(b => b.ProductId == productId)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(b => b.IsActive, false)
                    .SetProperty(b => b.QuantityOnHand, 0m)
                    .SetProperty(b => b.UpdatedAt, now),
                    cancellationToken);

            await _dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            TempData["InventoryMessage"] = "Product removed from stock successfully.";
            return RedirectToInventorySource(returnTo);
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync(CancellationToken.None);
            _logger.LogError(ex, "Failed to remove stock for product {ProductId}, batch {BatchId}, profile {ProfileId}.", productId, batchId, profileId);
            TempData["InventoryDetailsError"] = "The stock could not be removed. No inventory changes were saved.";
            return RedirectToAction(nameof(Details), new { productId, batchId, returnTo = NormalizeReturnTo(returnTo) });
        }
    }

    private static string NormalizeReturnTo(string? returnTo) =>
        returnTo?.Trim().ToLowerInvariant() switch
        {
            "expiry" => "expiry",
            "low-stock" => "low-stock",
            _ => string.Empty
        };

    private IActionResult RedirectToInventorySource(string? returnTo) =>
        NormalizeReturnTo(returnTo) switch
        {
            "expiry" => RedirectToAction("Index", "ExpiryProducts"),
            "low-stock" => RedirectToAction("Index", "LowStock"),
            _ => RedirectToAction(nameof(Index))
        };

    private static string? NormalizeText(string? value, int maxLength) =>
        NormalizeNullableText(value, maxLength);

    private static string? NormalizeNullableText(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var normalized = string.Join(
            " ",
            value.Trim().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

        return normalized.Length <= maxLength
            ? normalized
            : normalized[..maxLength].Trim();
    }

    private static DateOnly GetIndiaToday() =>
        DateOnly.FromDateTime(
            DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(5.5)).DateTime);

    private bool TryGetProfileId(out long profileId) =>
        long.TryParse(HttpContext.Session.GetString("ProfileId"), out profileId);

    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        if (!long.TryParse(HttpContext.Session.GetString("ProfileId"), out var profileId))
            return RedirectToAction("Login", "Account");

        var today = GetIndiaToday();
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
            var isLowStock = row.Quantity < 3;
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
            TotalInventoryValue = items.Sum(item => item.StockValue),
            Items = items
        });
    }
}
