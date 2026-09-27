using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PharmaFlow.Data;
using PharmaFlow.Filters;
using PharmaFlow.Models;
using PharmaFlow.Models.ViewModels;
using PharmaFlow.Services;

namespace PharmaFlow.Controllers;

[SessionAuthorize]
public sealed class InvoiceCaptureController : Controller
{
    private const int MaxParsedItems = 200;

    private readonly ApplicationDbContext _dbContext;
    private readonly ILogger<InvoiceCaptureController> _logger;

    public InvoiceCaptureController(
        ApplicationDbContext dbContext,
        ILogger<InvoiceCaptureController> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Parse(
        [FromBody] InvoiceParseRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetProfileId(out var profileId))
            return Unauthorized();

        if (request is null)
            return BadRequest(new { message = "Invalid invoice OCR request." });

        if (string.IsNullOrWhiteSpace(request.OcrText) || request.OcrText.Length > 200_000)
            return BadRequest(new { message = "OCR text is missing or too large." });

        var inputLines = request.Lines ?? [];

        if (inputLines.Count > 2_000)
            return BadRequest(new { message = "Too many OCR lines were supplied." });

        var sourceType = request.SourceType.Trim().ToLowerInvariant();
        if (sourceType is not ("camera" or "upload" or "pdf"))
            sourceType = "upload";

        var originalFileName = Path.GetFileName(request.OriginalFileName?.Trim() ?? "invoice");
        if (string.IsNullOrWhiteSpace(originalFileName))
            originalFileName = "invoice";

        if (originalFileName.Length > 255)
            originalFileName = originalFileName[..255];

        var lines = inputLines
            .Where(line => !string.IsNullOrWhiteSpace(line.Text))
            .Take(2_000)
            .ToList();

        if (lines.Count == 0)
        {
            lines =
            [
                new InvoiceOcrLineInput
                {
                    Text = request.OcrText,
                    Confidence = request.OcrConfidence
                }
            ];
        }

        var parsedItems = InvoiceOcrParser.Parse(lines);

        if (parsedItems.Count == 0)
            return BadRequest(new
            {
                message = "No possible stock rows were detected. Please retake the invoice with the full table visible and sharper text."
            });

        var now = DateTime.UtcNow;

        // Drafts contain only temporary OCR text. Remove abandoned drafts after 24 hours.
        await _dbContext.InvoiceImports
            .Where(item =>
                item.ProfileId == profileId &&
                item.Status == "draft" &&
                item.CreatedAt < now.AddHours(-24))
            .ExecuteDeleteAsync(cancellationToken);

        var import = new InvoiceImport
        {
            ProfileId = profileId,
            OriginalFileName = originalFileName,
            SourceType = sourceType,
            RawOcrText = request.OcrText.Trim(),
            OcrConfidence = Math.Clamp(request.OcrConfidence, 0m, 100m),
            Status = "draft",
            CreatedAt = now,
            UpdatedAt = now
        };

        foreach (var item in parsedItems.Take(MaxParsedItems))
        {
            var validation = ValidateParsedItem(item);

            import.Items.Add(new InvoiceImportItem
            {
                RowNumber = import.Items.Count + 1,
                RawLine = item.RawLine,
                ProductName = item.ProductName,
                BatchNumber = item.BatchNumber,
                ExpiryDate = item.ExpiryDate,
                Quantity = item.Quantity,
                Confidence = Math.Clamp(item.Confidence, 0m, 100m),
                ValidationStatus = validation.IsValid ? "ready" : "needs_review",
                ValidationMessage = validation.Message,
                CreatedAt = now,
                UpdatedAt = now
            });
        }

        if (import.Items.Count == 0)
            return BadRequest(new { message = "No invoice rows could be prepared." });

        _dbContext.InvoiceImports.Add(import);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return Ok(new
        {
            success = true,
            importId = import.ImportId,
            redirectUrl = Url.Action(nameof(Review), "InvoiceCapture", new { id = import.ImportId })
        });
    }

    [HttpGet]
    public async Task<IActionResult> Review(
        long id,
        CancellationToken cancellationToken)
    {
        if (!TryGetProfileId(out var profileId))
            return RedirectToAction("Login", "Account");

        var import = await _dbContext.InvoiceImports
            .AsNoTracking()
            .Include(item => item.Items.OrderBy(row => row.RowNumber))
            .FirstOrDefaultAsync(
                item => item.ImportId == id && item.ProfileId == profileId,
                cancellationToken);

        if (import is null)
            return NotFound();

        var model = await BuildReviewModelAsync(import, cancellationToken);
        return View(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Save(
        InvoiceReviewViewModel model,
        CancellationToken cancellationToken)
    {
        if (!TryGetProfileId(out var profileId))
            return Unauthorized();

        if (model.ImportId <= 0)
            return BadRequest(new { message = "Invalid invoice import." });

        var import = await _dbContext.InvoiceImports
            .Include(item => item.Items)
            .FirstOrDefaultAsync(
                item => item.ImportId == model.ImportId && item.ProfileId == profileId,
                cancellationToken);

        if (import is null)
            return NotFound();

        if (!string.Equals(import.Status, "draft", StringComparison.OrdinalIgnoreCase))
        {
            return RedirectToAction(nameof(Review), new { id = import.ImportId });
        }

        if (!ModelState.IsValid)
        {
            model.ErrorMessage = "Please correct the highlighted fields before saving stock.";
            return View("Review", model);
        }

        var postedItems = model.Items
            .Where(item => !item.Removed)
            .ToList();

        if (postedItems.Count == 0)
        {
            model.ErrorMessage = "Add at least one valid stock row before saving.";
            return View("Review", model);
        }

        if (postedItems.Count > MaxParsedItems)
        {
            model.ErrorMessage = $"An invoice can contain up to {MaxParsedItems} stock rows per import.";
            return View("Review", model);
        }

        var postedIds = postedItems
            .Where(item => item.ImportItemId > 0)
            .Select(item => item.ImportItemId)
            .ToHashSet();

        if (postedIds.Any(id => import.Items.All(dbItem => dbItem.ImportItemId != id)))
        {
            model.ErrorMessage = "One or more invoice rows are invalid. Please refresh the review page and try again.";
            return View("Review", model);
        }

        var validationErrors = new Dictionary<long, string>();

        foreach (var item in postedItems)
        {
            var rowError = ValidateReviewItem(item);
            if (rowError is not null)
                validationErrors[item.ImportItemId] = rowError;
        }

        if (validationErrors.Count > 0)
        {
            ApplyValidationErrors(model, validationErrors);
            model.ErrorMessage = "Please correct the highlighted rows before saving stock.";
            return View("Review", model);
        }

        var existingProducts = await _dbContext.Products
            .Where(product =>
                product.ProfileId == profileId &&
                product.IsActive)
            .OrderBy(product => product.ProductId)
            .ToListAsync(cancellationToken);

        var productGroups = existingProducts
            .GroupBy(product => NormalizeKey(product.ProductName))
            .ToDictionary(group => group.Key, group => group.ToList(), StringComparer.OrdinalIgnoreCase);

        var resolvedProducts = new Dictionary<string, Product>(StringComparer.OrdinalIgnoreCase);
        var newProducts = new Dictionary<string, Product>(StringComparer.OrdinalIgnoreCase);
        var rowProducts = new Dictionary<InvoiceReviewItemViewModel, Product>();

        foreach (var item in postedItems)
        {
            var productKey = NormalizeKey(item.ProductName);

            if (productGroups.TryGetValue(productKey, out var matches))
            {
                if (matches.Count > 1)
                {
                    validationErrors[item.ImportItemId] =
                        "Multiple active products have this name. Clean the product master first.";
                    continue;
                }

                resolvedProducts[productKey] = matches[0];
            }
            else if (!newProducts.TryGetValue(productKey, out var newProduct))
            {
                newProduct = new Product
                {
                    ProfileId = profileId,
                    ProductName = item.ProductName.Trim(),
                    ReorderLevel = 0m,
                    IsPrescriptionRequired = false,
                    IsActive = true,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                };

                newProducts[productKey] = newProduct;
                resolvedProducts[productKey] = newProduct;
            }

            rowProducts[item] = resolvedProducts[productKey];
        }

        if (validationErrors.Count > 0)
        {
            ApplyValidationErrors(model, validationErrors);
            model.ErrorMessage = "Some product names need attention before stock can be saved.";
            return View("Review", model);
        }

        var existingProductIds = resolvedProducts.Values
            .Where(product => product.ProductId > 0)
            .Select(product => product.ProductId)
            .Distinct()
            .ToList();

        var requestedBatchKeys = postedItems
            .Select(item => NormalizeKey(item.BatchNumber))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var existingBatches = existingProductIds.Count == 0 || requestedBatchKeys.Count == 0
            ? new List<ProductBatch>()
            : await _dbContext.ProductBatches
                .Where(batch =>
                    existingProductIds.Contains(batch.ProductId) &&
                    requestedBatchKeys.Contains(batch.BatchNumber.ToUpper()))
                .ToListAsync(cancellationToken);

        var batchLookup = existingBatches
            .GroupBy(batch => BatchKey(batch.ProductId, batch.BatchNumber))
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

        var aggregateRows = new List<AggregatedStockRow>();

        foreach (var group in postedItems.GroupBy(
                     item => $"{NormalizeKey(item.ProductName)}\u001F{NormalizeKey(item.BatchNumber)}",
                     StringComparer.OrdinalIgnoreCase))
        {
            var rows = group.ToList();
            var first = rows[0];
            var distinctExpiryDates = rows
                .Select(row => row.ExpiryDate)
                .Distinct()
                .ToList();

            if (distinctExpiryDates.Count != 1)
            {
                foreach (var row in rows)
                    validationErrors[row.ImportItemId] =
                        "The same product and batch has different expiry dates in this review.";
                continue;
            }

            var quantity = rows.Sum(row => row.Quantity.GetValueOrDefault());
            var product = rowProducts[first];
            aggregateRows.Add(new AggregatedStockRow(
                first,
                product,
                first.BatchNumber.Trim(),
                distinctExpiryDates[0]!.Value,
                quantity,
                rows));
        }

        foreach (var row in aggregateRows)
        {
            if (validationErrors.Count > 0)
                break;

            if (row.Product.ProductId <= 0)
                continue;

            var key = BatchKey(row.Product.ProductId, row.BatchNumber);
            if (!batchLookup.TryGetValue(key, out var existingBatch))
                continue;

            if (!existingBatch.IsActive)
            {
                foreach (var item in row.SourceItems)
                    validationErrors[item.ImportItemId] =
                        "This batch already exists but is inactive. Reactivate it or use a different batch number.";
            }
            else if (existingBatch.IsQuarantined)
            {
                foreach (var item in row.SourceItems)
                    validationErrors[item.ImportItemId] =
                        "This batch is quarantined and cannot receive new stock.";
            }
            else if (existingBatch.ExpiryDate != row.ExpiryDate)
            {
                foreach (var item in row.SourceItems)
                    validationErrors[item.ImportItemId] =
                        $"Existing batch expiry is {existingBatch.ExpiryDate:dd MMM yyyy}, which does not match the invoice.";
            }
            else if (existingBatch.QuantityOnHand + row.Quantity > 999_999_999m)
            {
                foreach (var item in row.SourceItems)
                    validationErrors[item.ImportItemId] =
                        "The resulting stock quantity is too large.";
            }
        }

        if (validationErrors.Count > 0)
        {
            ApplyValidationErrors(model, validationErrors);
            model.ErrorMessage = "One or more rows conflict with existing stock. No changes were saved.";
            return View("Review", model);
        }

        await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);

        try
        {
            foreach (var newProduct in newProducts.Values)
                _dbContext.Products.Add(newProduct);

            await _dbContext.SaveChangesAsync(cancellationToken);

            var newBatchLookup = new Dictionary<string, ProductBatch>(StringComparer.OrdinalIgnoreCase);

            foreach (var row in aggregateRows)
            {
                var key = BatchKey(row.Product.ProductId, row.BatchNumber);

                if (batchLookup.TryGetValue(key, out var existingBatch))
                {
                    existingBatch.QuantityOnHand += row.Quantity;
                    existingBatch.UpdatedAt = DateTime.UtcNow;
                    newBatchLookup[key] = existingBatch;
                }
                else if (!newBatchLookup.ContainsKey(key))
                {
                    var newBatch = new ProductBatch
                    {
                        ProductId = row.Product.ProductId,
                        BatchNumber = row.BatchNumber,
                        ExpiryDate = row.ExpiryDate,
                        QuantityOnHand = row.Quantity,
                        PurchaseUnitPrice = 0m,
                        SellingUnitPrice = 0m,
                        IsQuarantined = false,
                        IsActive = true,
                        CreatedAt = DateTime.UtcNow,
                        UpdatedAt = DateTime.UtcNow
                    };

                    _dbContext.ProductBatches.Add(newBatch);
                    newBatchLookup[key] = newBatch;
                }
                else
                {
                    newBatchLookup[key].QuantityOnHand += row.Quantity;
                }
            }

            await _dbContext.SaveChangesAsync(cancellationToken);

            var importItemsById = import.Items
                .ToDictionary(item => item.ImportItemId);

            var nextRowNumber = import.Items.Count == 0
                ? 1
                : import.Items.Max(item => item.RowNumber) + 1;

            foreach (var item in model.Items)
            {
                if (item.Removed)
                {
                    if (item.ImportItemId > 0 &&
                        importItemsById.TryGetValue(item.ImportItemId, out var removedItem))
                    {
                        removedItem.ValidationStatus = "removed";
                        removedItem.ValidationMessage = "Removed during review.";
                        removedItem.UpdatedAt = DateTime.UtcNow;
                    }

                    continue;
                }

                var product = rowProducts[item];
                var batch = newBatchLookup[BatchKey(product.ProductId, item.BatchNumber)];

                InvoiceImportItem entity;
                if (item.ImportItemId > 0 && importItemsById.TryGetValue(item.ImportItemId, out var existingItem))
                {
                    entity = existingItem;
                }
                else
                {
                    entity = new InvoiceImportItem
                    {
                        ImportId = import.ImportId,
                        RowNumber = nextRowNumber++
                    };
                    _dbContext.InvoiceImportItems.Add(entity);
                }

                entity.RawLine = string.IsNullOrWhiteSpace(entity.RawLine)
                    ? null
                    : entity.RawLine;
                entity.ProductName = item.ProductName.Trim();
                entity.BatchNumber = item.BatchNumber.Trim();
                entity.ExpiryDate = item.ExpiryDate;
                entity.Quantity = item.Quantity;
                entity.Confidence = Math.Clamp(item.Confidence, 0m, 100m);
                entity.ValidationStatus = "saved";
                entity.ValidationMessage = "Saved to stock.";
                entity.MatchedProductId = product.ProductId;
                entity.SavedBatchId = batch.BatchId;
                entity.UpdatedAt = DateTime.UtcNow;

                if (entity.CreatedAt == default)
                    entity.CreatedAt = DateTime.UtcNow;
            }

            import.Status = "saved";
            import.RawOcrText = null;
            import.ErrorMessage = null;
            import.UpdatedAt = DateTime.UtcNow;

            await _dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            return RedirectToAction(nameof(Review), new { id = import.ImportId });
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync(cancellationToken);
            _logger.LogError(ex, "Failed to save invoice import {ImportId} for profile {ProfileId}.", import.ImportId, profileId);

            model.ErrorMessage = "The invoice could not be saved. No stock changes were committed. Please try again.";
            return View("Review", model);
        }
    }

    private async Task<InvoiceReviewViewModel> BuildReviewModelAsync(
        InvoiceImport import,
        CancellationToken cancellationToken)
    {
        var productNames = import.Items
            .Select(item => NormalizeKey(item.ProductName))
            .Where(key => !string.IsNullOrWhiteSpace(key))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var products = productNames.Count == 0
            ? new List<Product>()
            : await _dbContext.Products
                .AsNoTracking()
                .Where(product =>
                    product.ProfileId == import.ProfileId &&
                    product.IsActive)
                .ToListAsync(cancellationToken);

        var productMap = products
            .GroupBy(product => NormalizeKey(product.ProductName))
            .ToDictionary(
                group => group.Key,
                group => group.ToList(),
                StringComparer.OrdinalIgnoreCase);

        var matchedProductIds = import.Items
            .Select(item =>
            {
                var key = NormalizeKey(item.ProductName);
                return productMap.TryGetValue(key, out var matches) && matches.Count == 1
                    ? matches[0].ProductId
                    : 0;
            })
            .Where(id => id > 0)
            .Distinct()
            .ToList();

        var batches = matchedProductIds.Count == 0
            ? new List<ProductBatch>()
            : await _dbContext.ProductBatches
                .AsNoTracking()
                .Where(batch => matchedProductIds.Contains(batch.ProductId))
                .ToListAsync(cancellationToken);

        var batchMap = batches
            .GroupBy(batch => BatchKey(batch.ProductId, batch.BatchNumber))
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

        var items = import.Items
            .OrderBy(item => item.RowNumber)
            .Select(item =>
            {
                var validation = GetReviewValidation(item, productMap, batchMap);

                return new InvoiceReviewItemViewModel
                {
                    ImportItemId = item.ImportItemId,
                    RowNumber = item.RowNumber,
                    RawLine = item.RawLine ?? string.Empty,
                    ProductName = item.ProductName,
                    BatchNumber = item.BatchNumber,
                    ExpiryDate = item.ExpiryDate,
                    Quantity = item.Quantity,
                    Confidence = item.Confidence,
                    ValidationStatus = validation.Status,
                    ValidationMessage = validation.Message,
                    MatchedProductId = validation.ProductId,
                    SavedBatchId = validation.BatchId
                };
            })
            .ToList();

        return new InvoiceReviewViewModel
        {
            ImportId = import.ImportId,
            OriginalFileName = import.OriginalFileName,
            SourceType = import.SourceType,
            OcrConfidence = import.OcrConfidence,
            Status = import.Status,
            ErrorMessage = import.ErrorMessage,
            Items = items
        };
    }

    private static (string Status, string Message, long? ProductId, long? BatchId) GetReviewValidation(
        InvoiceImportItem item,
        IReadOnlyDictionary<string, List<Product>> productMap,
        IReadOnlyDictionary<string, ProductBatch> batchMap)
    {
        if (string.IsNullOrWhiteSpace(item.ProductName) ||
            string.IsNullOrWhiteSpace(item.BatchNumber) ||
            !item.ExpiryDate.HasValue ||
            item.Quantity is not > 0)
        {
            return ("needs_review", item.ValidationMessage ?? "Complete the required fields.", null, null);
        }

        var productKey = NormalizeKey(item.ProductName);
        if (!productMap.TryGetValue(productKey, out var products) || products.Count == 0)
        {
            return ("ready", "New product will be created.", null, null);
        }

        if (products.Count > 1)
        {
            return ("needs_review", "Multiple active products have this name. Clean the product master first.", null, null);
        }

        var product = products[0];
        var batchKey = BatchKey(product.ProductId, item.BatchNumber);

        if (!batchMap.TryGetValue(batchKey, out var batch))
            return ("ready", "Existing product. New batch will be created.", product.ProductId, null);

        if (!batch.IsActive)
            return ("needs_review", "This batch already exists but is inactive.", product.ProductId, batch.BatchId);

        if (batch.IsQuarantined)
            return ("needs_review", "This batch is quarantined and cannot receive new stock.", product.ProductId, batch.BatchId);

        if (batch.ExpiryDate != item.ExpiryDate.Value)
            return (
                "needs_review",
                $"Existing batch expiry is {batch.ExpiryDate:dd MMM yyyy}, which does not match.",
                product.ProductId,
                batch.BatchId);

        return ("ready", "Existing product and batch. Quantity will be added to current stock.", product.ProductId, batch.BatchId);
    }

    private static (bool IsValid, string Message) ValidateParsedItem(
        InvoiceOcrParser.ParsedInvoiceItem item)
    {
        if (string.IsNullOrWhiteSpace(item.ProductName))
            return (false, "Product name was not detected.");

        if (string.IsNullOrWhiteSpace(item.BatchNumber))
            return (false, "Batch number was not detected.");

        if (!item.ExpiryDate.HasValue)
            return (false, "Expiry date was not detected.");

        if (item.Quantity is not > 0)
            return (false, "Quantity was not detected.");

        if (item.ExpiryDate.Value < DateOnly.FromDateTime(DateTime.UtcNow))
            return (false, "Expiry date is already in the past.");

        if (item.ProductName.Length > 200 || item.BatchNumber.Length > 100)
            return (false, "Detected text is longer than the allowed field length.");

        if (item.Quantity.Value > 1_000_000m)
            return (false, "Detected quantity is too large.");

        return (true, "All required fields were detected.");
    }

    private static string? ValidateReviewItem(InvoiceReviewItemViewModel item)
    {
        if (string.IsNullOrWhiteSpace(item.ProductName))
            return "Product name is required.";

        if (item.ProductName.Length > 200)
            return "Product name must be 200 characters or fewer.";

        if (string.IsNullOrWhiteSpace(item.BatchNumber))
            return "Batch number is required.";

        if (item.BatchNumber.Length > 100)
            return "Batch number must be 100 characters or fewer.";

        if (!item.ExpiryDate.HasValue)
            return "Expiry date is required.";

        if (item.ExpiryDate.Value < DateOnly.FromDateTime(DateTime.UtcNow))
            return "Expiry date cannot be earlier than today.";

        if (item.Quantity is not > 0)
            return "Quantity must be greater than zero.";

        if (item.Quantity.Value > 1_000_000m)
            return "Quantity is too large.";

        return null;
    }

    private static void ApplyValidationErrors(
        InvoiceReviewViewModel model,
        IReadOnlyDictionary<long, string> errors)
    {
        foreach (var item in model.Items)
        {
            if (errors.TryGetValue(item.ImportItemId, out var error))
            {
                item.ValidationStatus = "needs_review";
                item.ValidationMessage = error;
            }
        }
    }

    private static string NormalizeKey(string? value)
    {
        var text = value?.Trim().ToUpperInvariant() ?? string.Empty;
        return System.Text.RegularExpressions.Regex.Replace(text, @"\s+", " ");
    }

    private static string BatchKey(long productId, string batchNumber) =>
        $"{productId}\u001F{NormalizeKey(batchNumber)}";

    private bool TryGetProfileId(out long profileId) =>
        long.TryParse(HttpContext.Session.GetString("ProfileId"), out profileId);

    private sealed record AggregatedStockRow(
        InvoiceReviewItemViewModel FirstItem,
        Product Product,
        string BatchNumber,
        DateOnly ExpiryDate,
        decimal Quantity,
        IReadOnlyList<InvoiceReviewItemViewModel> SourceItems);
}
