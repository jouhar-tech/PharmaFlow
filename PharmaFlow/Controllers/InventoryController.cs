using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PharmaFlow.Data;
using PharmaFlow.Filters;
using PharmaFlow.Models;
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
    public async Task<IActionResult> SearchProducts(
        string? query,
        CancellationToken cancellationToken)
    {
        if (!TryGetProfileId(out var profileId))
            return RedirectToAction("Login", "Account");

        var normalizedQuery = NormalizeNullableText(query, 100) ?? string.Empty;

        var productsQuery = _dbContext.Products
            .AsNoTracking()
            .Where(p =>
                p.ProfileId == profileId &&
                (
                    !p.Batches.Any(b =>
                        b.IsActive &&
                        !b.IsQuarantined &&
                        b.QuantityOnHand > 0)
                    || !p.IsActive));

        if (!string.IsNullOrWhiteSpace(normalizedQuery))
        {
            var search = normalizedQuery.ToLower();
            productsQuery = productsQuery.Where(p =>
                p.ProductName.ToLower().Contains(search) ||
                (p.GenericName != null && p.GenericName.ToLower().Contains(search)) ||
                (p.BrandName != null && p.BrandName.ToLower().Contains(search)) ||
                (p.Barcode != null && p.Barcode.ToLower().Contains(search)));
        }

        var products = await productsQuery
            .OrderBy(p => p.ProductName)
            .Take(100)
            .Select(p => new InventoryProductSearchItemViewModel
            {
                ProductId = p.ProductId,
                ProductName = p.ProductName,
                GenericName = p.GenericName,
                BrandName = p.BrandName,
                Barcode = p.Barcode,
                LatestMrp = p.Batches
                    .OrderByDescending(b => b.CreatedAt)
                    .Select(b => (decimal?)b.SellingUnitPrice)
                    .FirstOrDefault()
            })
            .ToListAsync(cancellationToken);

        ViewData["Title"] = "Search Products";
        return View(new InventoryProductSearchViewModel
        {
            Query = normalizedQuery,
            Products = products
        });
    }

    [HttpGet]
    public async Task<IActionResult> AddItem(
        long? productId,
        CancellationToken cancellationToken)
    {
        if (!TryGetProfileId(out var profileId))
            return RedirectToAction("Login", "Account");

        var model = new InventoryAddItemViewModel
        {
            ExistingProductId = productId,
            ExpiryDate = DateOnly.FromDateTime(DateTime.UtcNow).AddYears(2)
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

        model.ProductName = NormalizeText(model.ProductName, 200) ?? string.Empty;

        if (model.ExpiryDate == default)
            model.ExpiryDate = DateOnly.FromDateTime(DateTime.UtcNow).AddYears(2);

        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        if (model.ExpiryDate < today)
            ModelState.AddModelError(nameof(model.ExpiryDate), "Expiry cannot be earlier than today.");

        if (model.Quantity <= 0 || model.Quantity > 999_999_999m)
            ModelState.AddModelError(nameof(model.Quantity), "Enter a valid quantity.");

        if (model.Mrp <= 0 || model.Mrp > 999_999_999m)
            ModelState.AddModelError(nameof(model.Mrp), "Enter a valid MRP.");

        if (!ModelState.IsValid)
            return View(model);

        await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);

        Product? product = null;

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
            Batches = batches
        };

        ViewData["Title"] = "Product Details";
        return View(model);
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
            .Include(b => b.Product)
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

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        if (expiryDate < today)
            return BadRequest(new { message = "Expiry date cannot be earlier than today." });

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

        var batches = await _dbContext.ProductBatches
            .Where(b => b.ProductId == productId)
            .ToListAsync(cancellationToken);

        product.IsActive = false;
        product.UpdatedAt = DateTime.UtcNow;

        foreach (var batch in batches)
        {
            batch.IsActive = false;
            batch.UpdatedAt = DateTime.UtcNow;
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        return RedirectToAction(nameof(Index));
    }

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

    private bool TryGetProfileId(out long profileId) =>
        long.TryParse(HttpContext.Session.GetString("ProfileId"), out profileId);

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
            Items = items
        });
    }
}
