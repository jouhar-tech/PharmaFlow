using System.Data;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Npgsql.EntityFrameworkCore.PostgreSQL;
using PharmaFlow.Data;
using PharmaFlow.Filters;
using PharmaFlow.Models;
using PharmaFlow.Models.ViewModels;
using PharmaFlow.Services;

namespace PharmaFlow.Controllers;

[SessionAuthorize]
public sealed class BillingController : Controller
{
    private readonly ApplicationDbContext _dbContext;
    private readonly IGlobalProductCatalogService _globalProductCatalogService;
    private readonly ILogger<BillingController> _logger;

    public BillingController(
        ApplicationDbContext dbContext,
        IGlobalProductCatalogService globalProductCatalogService,
        ILogger<BillingController> logger)
    {
        _dbContext = dbContext;
        _globalProductCatalogService = globalProductCatalogService;
        _logger = logger;
    }

    [HttpGet]
    public IActionResult Create()
    {
        return View(new CreateBillViewModel());
    }

    [HttpGet]
    public async Task<IActionResult> Search(
        string? q,
        CancellationToken cancellationToken)
    {
        if (!TryGetProfileId(out var profileId))
            return Unauthorized();

        var searchTerm = q?.Trim();
        if (string.IsNullOrWhiteSpace(searchTerm))
            return Ok(Array.Empty<BillingSearchResultViewModel>());

        if (searchTerm.Length > 80)
            searchTerm = searchTerm[..80];

        var pattern = $"%{searchTerm.Replace("%", "\\%").Replace("_", "\\_")}%";

        // Product search is read-only, so use a bounded PostgreSQL retry strategy
        // without changing the explicit transaction behavior used by billing generation.
        var searchExecutionStrategy = new NpgsqlRetryingExecutionStrategy(
            _dbContext,
            maxRetryCount: 3);

        // Return only the fields required by the billing search UI.
        var products = await searchExecutionStrategy.ExecuteAsync(
            () => _dbContext.Products
                .AsNoTracking()
                .Where(p =>
                    p.ProfileId == profileId &&
                    p.IsActive &&
                    (EF.Functions.ILike(p.ProductName, pattern, "\\") ||
                     (p.GenericName != null && EF.Functions.ILike(p.GenericName, pattern, "\\")) ||
                     (p.BrandName != null && EF.Functions.ILike(p.BrandName, pattern, "\\")) ||
                     (p.Barcode != null && EF.Functions.ILike(p.Barcode, pattern, "\\"))))
                .OrderBy(p => p.ProductName)
                .Take(12)
                .Select(p => new
                {
                    p.ProductId,
                    p.ProductName,
                    p.HsnCode,
                    p.GstRate
                })
                .ToListAsync(cancellationToken));

        if (products.Count == 0)
            return Ok(Array.Empty<BillingSearchResultViewModel>());

        var productIds = products.Select(p => p.ProductId).ToList();
        var todayIndia = DateOnly.FromDateTime(
            DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(5.5)).DateTime);

        // EF Core cannot translate grouped Top-N consistently across providers.
        // Fetch only the required saleable batch columns, then cap batches per
        // matched product in memory. At most 12 products are searched.
        var batches = await searchExecutionStrategy.ExecuteAsync(
            () => _dbContext.ProductBatches
                .AsNoTracking()
                .Where(b =>
                    productIds.Contains(b.ProductId) &&
                    b.IsActive &&
                    !b.IsQuarantined &&
                    b.QuantityOnHand > 0 &&
                    b.ExpiryDate >= todayIndia)
                .OrderBy(b => b.ProductId)
                .ThenBy(b => b.ExpiryDate)
                .ThenBy(b => b.BatchNumber)
                .Select(b => new
                {
                    b.ProductId,
                    b.BatchId,
                    b.BatchNumber,
                    b.ExpiryDate,
                    b.QuantityOnHand,
                    b.SellingUnitPrice
                })
                .ToListAsync(cancellationToken));

        var batchesByProduct = batches
            .GroupBy(b => b.ProductId)
            .ToDictionary(g => g.Key, g => g.Take(8).ToList());

        var results = products
            .Where(p => batchesByProduct.ContainsKey(p.ProductId))
            .Select(p => new BillingSearchResultViewModel
            {
                ProductId = p.ProductId,
                ProductName = p.ProductName,
                HsnCode = p.HsnCode,
                GstRate = p.GstRate.GetValueOrDefault(),
                Batches = batchesByProduct[p.ProductId]
                    .Select(b => new BillingBatchViewModel
                    {
                        BatchId = b.BatchId,
                        BatchNumber = b.BatchNumber,
                        ExpiryDate = b.ExpiryDate,
                        QuantityOnHand = b.QuantityOnHand,
                        SellingUnitPrice = b.SellingUnitPrice,
                        Mrp = b.SellingUnitPrice
                    })
                    .ToList()
            })
            .ToList();

        return Ok(results);
    }

    [HttpGet]
    public async Task<IActionResult> SearchCatalog(
        string? q,
        CancellationToken cancellationToken)
    {
        if (!TryGetProfileId(out _))
            return Unauthorized();

        var searchTerm = q?.Trim();
        if (string.IsNullOrWhiteSpace(searchTerm) || searchTerm.Length < 2)
            return Ok(Array.Empty<BillingCatalogSearchResultViewModel>());

        if (searchTerm.Length > 80)
            searchTerm = searchTerm[..80];

        var results = await _globalProductCatalogService.SearchAsync(searchTerm, cancellationToken);

        return Ok(results
            .Where(item => !string.IsNullOrWhiteSpace(item.ProductName))
            .Take(30)
            .Select(item => new BillingCatalogSearchResultViewModel
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
                Barcode = item.Barcode,
                GstRate = item.GstRate,
                IsPrescriptionRequired = item.IsPrescriptionRequired
            })
            .ToList());
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AddCatalogStock(
        [FromBody] BillingAddCatalogStockRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetProfileId(out var profileId))
            return Unauthorized();

        var source = request.Source?.Trim() ?? string.Empty;
        var externalId = request.ExternalId?.Trim() ?? string.Empty;
        var batchNumber = request.BatchNumber?.Trim() ?? string.Empty;
        var todayIndia = DateOnly.FromDateTime(
            DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(5.5)).DateTime);

        if (string.IsNullOrWhiteSpace(source) ||
            string.IsNullOrWhiteSpace(externalId) ||
            externalId.Length > 160)
            return BadRequest(new { message = "The selected catalogue product is invalid. Search and select it again." });

        if (string.IsNullOrWhiteSpace(batchNumber) || batchNumber.Length > 100)
            return BadRequest(new { message = "Enter the batch or lot number printed on the physical product." });

        if (request.ExpiryDate == default || request.ExpiryDate < todayIndia)
            return BadRequest(new { message = "Enter the actual expiry date. Expired products cannot be added for sale." });

        if (request.AvailableQuantity <= 0m || request.AvailableQuantity > 999_999m)
            return BadRequest(new { message = "Enter the total usable quantity physically available on the shelf." });

        if (request.QuantityToBill <= 0m ||
            request.QuantityToBill > request.AvailableQuantity ||
            request.QuantityToBill > 999_999m)
            return BadRequest(new { message = "Quantity to bill must be greater than zero and cannot exceed the physical quantity available." });

        if (request.SellingUnitPrice <= 0m || request.SellingUnitPrice > 999_999_999m)
            return BadRequest(new { message = "Enter a valid per-unit selling price." });

        if (request.PurchaseUnitPrice is < 0m or > 999_999_999m)
            return BadRequest(new { message = "Purchase price must be zero or a valid positive amount." });

        if (request.GstRate is < 0m or > 100m)
            return BadRequest(new { message = "GST rate must be between 0 and 100." });

        var globalProduct = await _globalProductCatalogService.GetAsync(
            source,
            externalId,
            request.CatalogId,
            cancellationToken);

        if (globalProduct is null)
            return NotFound(new { message = "The catalogue result has expired or is no longer available. Search again." });

        if (globalProduct.IsPrescriptionRequired && !request.PrescriptionConfirmed)
        {
            return BadRequest(new
            {
                message = "This catalogue product is marked prescription-required. Verify the prescription before adding it to a bill."
            });
        }

        await using var transaction = await _dbContext.Database.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken);

        try
        {
            Product? product = null;

            if (globalProduct.CatalogId.HasValue)
            {
                product = await _dbContext.Products.FirstOrDefaultAsync(
                    item => item.ProfileId == profileId &&
                            item.CatalogId == globalProduct.CatalogId.Value,
                    cancellationToken);
            }

            if (product is null && !string.IsNullOrWhiteSpace(globalProduct.Barcode))
            {
                product = await _dbContext.Products.FirstOrDefaultAsync(
                    item => item.ProfileId == profileId &&
                            item.Barcode == globalProduct.Barcode,
                    cancellationToken);
            }

            if (product is null && string.IsNullOrWhiteSpace(globalProduct.Barcode))
            {
                // A pharmacist may already have created this product manually.
                // Reuse it instead of creating a duplicate when a catalogue record
                // is selected later. Match name + manufacturer + strength + pack.
                var productName = globalProduct.ProductName.ToLowerInvariant();
                var manufacturer = globalProduct.Manufacturer ?? string.Empty;
                var strength = globalProduct.Strength ?? string.Empty;
                var packSize = globalProduct.PackSize ?? string.Empty;

                product = await _dbContext.Products.FirstOrDefaultAsync(
                    item => item.ProfileId == profileId &&
                            item.ProductName.ToLower() == productName &&
                            (item.Manufacturer ?? string.Empty) == manufacturer &&
                            (item.Strength ?? string.Empty) == strength &&
                            (item.PackSize ?? string.Empty) == packSize,
                    cancellationToken);
            }

            if (product is not null &&
                globalProduct.CatalogId.HasValue &&
                !product.CatalogId.HasValue)
            {
                var catalogAlreadyLinked = await _dbContext.Products.AnyAsync(
                    item => item.ProfileId == profileId &&
                            item.CatalogId == globalProduct.CatalogId.Value &&
                            item.ProductId != product.ProductId,
                    cancellationToken);

                if (!catalogAlreadyLinked)
                    product.CatalogId = globalProduct.CatalogId.Value;
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
                    GstRate = request.GstRate ?? globalProduct.GstRate,
                    IsPrescriptionRequired = globalProduct.IsPrescriptionRequired,
                    IsActive = true,
                    ReorderLevel = 0m,
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

                // Preserve pharmacy-maintained details. Fill only fields that are
                // currently blank, and never overwrite a GST rate already in use.
                product.GenericName ??= globalProduct.GenericName;
                product.BrandName ??= globalProduct.BrandName;
                product.DosageForm ??= globalProduct.DosageForm;
                product.Strength ??= globalProduct.Strength;
                product.PackSize ??= globalProduct.PackSize;
                product.Barcode ??= globalProduct.Barcode;
                product.Manufacturer ??= globalProduct.Manufacturer;
                product.HsnCode ??= globalProduct.HsnCode;
                product.GstRate ??= request.GstRate ?? globalProduct.GstRate;
            }

            var effectiveGstRate = product.GstRate ?? request.GstRate ?? globalProduct.GstRate;
            if (!effectiveGstRate.HasValue ||
                effectiveGstRate.Value < 0m ||
                effectiveGstRate.Value > 100m)
            {
                await transaction.RollbackAsync(CancellationToken.None);
                return BadRequest(new
                {
                    message = "GST rate is missing. Enter and verify the correct GST rate for this product before adding it to a bill."
                });
            }

            var existingBatch = await _dbContext.ProductBatches.FirstOrDefaultAsync(
                batch => batch.ProductId == product.ProductId &&
                         batch.BatchNumber.ToLower() == batchNumber.ToLower(),
                cancellationToken);

            if (existingBatch is not null)
            {
                if (existingBatch.IsQuarantined)
                {
                    await transaction.RollbackAsync(CancellationToken.None);
                    return Conflict(new
                    {
                        message = "This batch is quarantined and cannot be sold. Review it in Inventory before billing."
                    });
                }

                if (existingBatch.ExpiryDate != request.ExpiryDate)
                {
                    await transaction.RollbackAsync(CancellationToken.None);
                    return Conflict(new
                    {
                        message = "This batch number already exists with a different expiry date. Check the batch details in Inventory."
                    });
                }

                // AvailableQuantity is the user's physical shelf count, not an
                // incremental receipt. Reconcile the stored count to that number.
                existingBatch.QuantityOnHand = decimal.Round(
                    request.AvailableQuantity,
                    2,
                    MidpointRounding.AwayFromZero);
                existingBatch.SellingUnitPrice = decimal.Round(
                    request.SellingUnitPrice,
                    2,
                    MidpointRounding.AwayFromZero);
                if (request.PurchaseUnitPrice.HasValue)
                {
                    existingBatch.PurchaseUnitPrice = decimal.Round(
                        request.PurchaseUnitPrice.Value,
                        2,
                        MidpointRounding.AwayFromZero);
                }
                existingBatch.IsActive = true;
                existingBatch.UpdatedAt = DateTime.UtcNow;
            }
            else
            {
                existingBatch = new ProductBatch
                {
                    ProductId = product.ProductId,
                    BatchNumber = batchNumber,
                    ExpiryDate = request.ExpiryDate,
                    QuantityOnHand = decimal.Round(
                        request.AvailableQuantity,
                        2,
                        MidpointRounding.AwayFromZero),
                    PurchaseUnitPrice = decimal.Round(
                        request.PurchaseUnitPrice ?? 0m,
                        2,
                        MidpointRounding.AwayFromZero),
                    SellingUnitPrice = decimal.Round(
                        request.SellingUnitPrice,
                        2,
                        MidpointRounding.AwayFromZero),
                    IsQuarantined = false,
                    IsActive = true,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                };

                _dbContext.ProductBatches.Add(existingBatch);
            }

            await _dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            return Ok(new
            {
                success = true,
                message = "Physical stock was added to inventory and is ready for billing.",
                batch = new
                {
                    batchId = existingBatch.BatchId,
                    productName = product.ProductName,
                    batchNumber = existingBatch.BatchNumber,
                    expiryDate = existingBatch.ExpiryDate,
                    availableQty = existingBatch.QuantityOnHand,
                    unitPrice = existingBatch.SellingUnitPrice,
                    mrp = existingBatch.SellingUnitPrice,
                    gstRate = effectiveGstRate.Value
                },
                quantityToBill = request.QuantityToBill
            });
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync(CancellationToken.None);
            _logger.LogError(
                ex,
                "Failed to add catalogue product stock to the bill for profile {ProfileId}.",
                profileId);

            return StatusCode(StatusCodes.Status500InternalServerError, new
            {
                message = "The product could not be added to inventory. No stock changes were committed. Please retry."
            });
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Generate(
        CreateBillViewModel model,
        CancellationToken cancellationToken)
    {
        if (!TryGetProfileId(out var profileId))
            return Unauthorized();

        var paymentMethod = NormalizePaymentMethod(model.PaymentMethod);
        var customerName = NormalizeOptionalText(model.CustomerName, 120);
        var phoneNumber = NormalizePhoneNumber(model.PhoneNumber);

        if (!string.IsNullOrWhiteSpace(model.PhoneNumber) && phoneNumber is null)
        {
            TempData["BillingError"] = "Enter a valid 10-digit Indian mobile number.";
            return RedirectToAction(nameof(Create));
        }

        if (paymentMethod == "Udhaar" && customerName is null && phoneNumber is null)
        {
            TempData["BillingError"] = "For Udhaar, enter the customer name or a valid 10-digit mobile number. The customer will be linked automatically.";
            return RedirectToAction(nameof(Create));
        }

        List<BillingLineInput> requestedLines;
        try
        {
            requestedLines = string.IsNullOrWhiteSpace(model.ItemsJson)
                ? []
                : JsonSerializer.Deserialize<List<BillingLineInput>>(
                    model.ItemsJson,
                    new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    }) ?? [];
        }
        catch (JsonException)
        {
            TempData["BillingError"] = "The selected medicines could not be read. Please add them again.";
            return RedirectToAction(nameof(Create));
        }

        if (requestedLines.Count == 0)
        {
            TempData["BillingError"] = "Add at least one medicine before generating the bill.";
            return RedirectToAction(nameof(Create));
        }

        if (requestedLines.Count > 50)
        {
            TempData["BillingError"] = "A single bill can contain at most 50 different stock lines.";
            return RedirectToAction(nameof(Create));
        }

        if (requestedLines.Any(line =>
                line.BatchId <= 0 ||
                line.Quantity <= 0m ||
                line.Quantity > 999_999m))
        {
            TempData["BillingError"] = "One or more bill quantities are invalid.";
            return RedirectToAction(nameof(Create));
        }

        var lines = requestedLines
            .GroupBy(line => line.BatchId)
            .Select(group => new BillingLineInput
            {
                BatchId = group.Key,
                Quantity = decimal.Round(
                    group.Sum(line => line.Quantity),
                    2,
                    MidpointRounding.AwayFromZero)
            })
            .ToList();

        if (lines.Any(line => line.BatchId <= 0 || line.Quantity <= 0m || line.Quantity > 999_999m))
        {
            TempData["BillingError"] = "One or more bill quantities are invalid.";
            return RedirectToAction(nameof(Create));
        }

        lines = lines.OrderBy(line => line.BatchId).ToList();

        var profile = await _dbContext.Profiles
            .AsNoTracking()
            .Where(p => p.Id == profileId)
            .Select(p => new
            {
                p.BusinessName,
                p.PhoneNumber,
                p.Email
            })
            .SingleOrDefaultAsync(cancellationToken);

        if (profile is null)
            return Unauthorized();

        await using var transaction = await _dbContext.Database.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken);

        try
        {
            var batchIds = lines.Select(line => line.BatchId).ToList();

            var batches = await _dbContext.ProductBatches
                .Include(b => b.Product)
                .Where(b =>
                    batchIds.Contains(b.BatchId) &&
                    b.Product.ProfileId == profileId)
                .ToListAsync(cancellationToken);

            if (batches.Count != batchIds.Count)
                return await BillingErrorAsync(transaction, "One or more selected stock batches are no longer available.");

            var batchById = batches.ToDictionary(b => b.BatchId);
            var today = DateOnly.FromDateTime(
                DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(5.5)).DateTime);

            foreach (var line in lines)
            {
                var batch = batchById[line.BatchId];

                if (!batch.IsActive || batch.IsQuarantined)
                    return await BillingErrorAsync(transaction, $"Batch {batch.BatchNumber} is not available for sale.");

                if (batch.ExpiryDate < today)
                    return await BillingErrorAsync(transaction, $"Batch {batch.BatchNumber} has expired and cannot be sold.");

                if (batch.QuantityOnHand < line.Quantity)
                    return await BillingErrorAsync(
                        transaction,
                        $"{batch.Product.ProductName} has only {batch.QuantityOnHand:0.##} units available.");

                if (batch.SellingUnitPrice <= 0)
                    return await BillingErrorAsync(
                        transaction,
                        $"{batch.Product.ProductName} does not have a selling price configured.");

                const decimal MaxBillAmount = 999_999_999_999.99m;
                var maximumLineTotal = decimal.Round(
                    batch.SellingUnitPrice * line.Quantity,
                    2,
                    MidpointRounding.AwayFromZero);

                if (maximumLineTotal > MaxBillAmount)
                    return await BillingErrorAsync(
                        transaction,
                        $"{batch.Product.ProductName} exceeds the maximum billable amount for a single line.");
            }

            var itemResults = new List<CreateBillItemViewModel>();
            decimal taxableTotal = 0m;
            decimal cgstTotal = 0m;
            decimal sgstTotal = 0m;
            decimal igstTotal = 0m;
            decimal grossTotal = 0m;

            foreach (var line in lines)
            {
                var batch = batchById[line.BatchId];
                var gstRate = Math.Max(0m, batch.Product.GstRate.GetValueOrDefault());
                var unitPrice = decimal.Round(batch.SellingUnitPrice, 2, MidpointRounding.AwayFromZero);
                var lineTotal = decimal.Round(unitPrice * line.Quantity, 2, MidpointRounding.AwayFromZero);

                // Retail selling price is treated as GST-inclusive.
                var taxable = gstRate > 0
                    ? decimal.Round(lineTotal / (1m + gstRate / 100m), 2, MidpointRounding.AwayFromZero)
                    : lineTotal;

                var gstAmount = decimal.Round(lineTotal - taxable, 2, MidpointRounding.AwayFromZero);
                var cgst = decimal.Round(gstAmount / 2m, 2, MidpointRounding.AwayFromZero);
                var sgst = decimal.Round(gstAmount - cgst, 2, MidpointRounding.AwayFromZero);

                taxableTotal += taxable;
                cgstTotal += cgst;
                sgstTotal += sgst;
                grossTotal += lineTotal;

                itemResults.Add(new CreateBillItemViewModel
                {
                    BatchId = batch.BatchId,
                    ProductName = batch.Product.ProductName,
                    BatchNumber = batch.BatchNumber,
                    ExpiryDate = batch.ExpiryDate,
                    Quantity = line.Quantity,
                    Mrp = unitPrice,
                    UnitPrice = unitPrice,
                    GstRate = gstRate,
                    GstAmount = gstAmount,
                    LineTotal = lineTotal
                });
            }

            cgstTotal = decimal.Round(cgstTotal, 2, MidpointRounding.AwayFromZero);
            sgstTotal = decimal.Round(sgstTotal, 2, MidpointRounding.AwayFromZero);
            taxableTotal = decimal.Round(taxableTotal, 2, MidpointRounding.AwayFromZero);
            grossTotal = decimal.Round(grossTotal, 2, MidpointRounding.AwayFromZero);
            var gstTotal = decimal.Round(cgstTotal + sgstTotal + igstTotal, 2, MidpointRounding.AwayFromZero);

            Customer? customer = null;
            if (customerName is not null || phoneNumber is not null)
            {
                if (phoneNumber is not null)
                {
                    customer = await _dbContext.Customers
                        .FirstOrDefaultAsync(c =>
                            c.ProfileId == profileId &&
                            c.PhoneNumber == phoneNumber,
                            cancellationToken);
                }

                if (customer is null && customerName is not null)
                {
                    customer = await _dbContext.Customers
                        .FirstOrDefaultAsync(c =>
                            c.ProfileId == profileId &&
                            c.FullName != null &&
                            c.FullName.ToLower() == customerName.ToLower(),
                            cancellationToken);
                }

                if (customer is null)
                {
                    customer = new Customer
                    {
                        ProfileId = profileId,
                        FullName = customerName,
                        PhoneNumber = phoneNumber,
                        CreatedAt = DateTime.UtcNow,
                        UpdatedAt = DateTime.UtcNow
                    };
                    _dbContext.Customers.Add(customer);
                }
                else
                {
                    customer.FullName = customerName ?? customer.FullName;
                    customer.PhoneNumber = phoneNumber ?? customer.PhoneNumber;
                    customer.UpdatedAt = DateTime.UtcNow;
                }
            }

            if (customer is not null && customer.CustomerId == 0)
                await _dbContext.SaveChangesAsync(cancellationToken);

            var billNumber = await GenerateUniqueBillNumberAsync(cancellationToken);
            var now = DateTime.UtcNow;

            var bill = new SalesBill
            {
                ProfileId = profileId,
                CustomerId = customer?.CustomerId,
                BillNumber = billNumber,
                CustomerName = customerName,
                CustomerPhone = phoneNumber,
                Subtotal = grossTotal,
                TaxableAmount = taxableTotal,
                CgstAmount = cgstTotal,
                SgstAmount = sgstTotal,
                IgstAmount = igstTotal,
                GstAmount = gstTotal,
                TotalAmount = grossTotal,
                PaymentMethod = paymentMethod,
                Status = "Completed",
                CreatedAt = now
            };

            _dbContext.SalesBills.Add(bill);
            await _dbContext.SaveChangesAsync(cancellationToken);

            if (paymentMethod == "Udhaar" && customer is not null && grossTotal > 0)
            {
                _dbContext.CustomerLedgerEntries.Add(new CustomerLedgerEntry
                {
                    ProfileId = profileId,
                    CustomerId = customer.CustomerId,
                    BillId = bill.BillId,
                    EntryType = "UdhaarSale",
                    BalanceChange = grossTotal,
                    Description = $"Udhaar sale · Bill {bill.BillNumber}",
                    CreatedAt = now
                });

                await _dbContext.SaveChangesAsync(cancellationToken);
            }

            foreach (var line in itemResults)
            {
                var billLine = new SalesBillItem
                {
                    BillId = bill.BillId,
                    ProductId = batchById[line.BatchId].ProductId,
                    BatchId = line.BatchId,
                    ProductName = line.ProductName,
                    BatchNumber = line.BatchNumber,
                    ExpiryDate = line.ExpiryDate,
                    Quantity = line.Quantity,
                    UnitPrice = line.UnitPrice,
                    PurchaseUnitPrice = batchById[line.BatchId].PurchaseUnitPrice,
                    Mrp = line.Mrp,
                    GstRate = line.GstRate,
                    TaxableAmount = line.GstRate > 0
                        ? decimal.Round(line.LineTotal / (1m + line.GstRate / 100m), 2, MidpointRounding.AwayFromZero)
                        : line.LineTotal,
                    CgstAmount = decimal.Round(line.GstAmount / 2m, 2, MidpointRounding.AwayFromZero),
                    SgstAmount = decimal.Round(
                        line.GstAmount - decimal.Round(line.GstAmount / 2m, 2, MidpointRounding.AwayFromZero),
                        2,
                        MidpointRounding.AwayFromZero),
                    IgstAmount = 0m,
                    GstAmount = line.GstAmount,
                    LineTotal = line.LineTotal
                };

                _dbContext.SalesBillItems.Add(billLine);
            }

            await _dbContext.SaveChangesAsync(cancellationToken);

            foreach (var line in lines)
            {
                var affected = await _dbContext.ProductBatches
                    .Where(b =>
                        b.BatchId == line.BatchId &&
                        b.Product.ProfileId == profileId &&
                        b.IsActive &&
                        !b.IsQuarantined &&
                        b.ExpiryDate >= today &&
                        b.QuantityOnHand >= line.Quantity)
                    .ExecuteUpdateAsync(setters => setters
                        .SetProperty(b => b.QuantityOnHand, b => b.QuantityOnHand - line.Quantity)
                        .SetProperty(b => b.UpdatedAt, DateTime.UtcNow), cancellationToken);

                if (affected != 1)
                    throw new InvalidOperationException(
                        $"Stock changed while generating bill for batch {line.BatchId}. Please retry.");
            }

            await transaction.CommitAsync(cancellationToken);

            var generated = new GeneratedBillViewModel
            {
                BillId = bill.BillId,
                BusinessName = string.IsNullOrWhiteSpace(profile?.BusinessName) ? "PharmaHex" : profile.BusinessName!,
                BusinessPhone = profile?.PhoneNumber,
                BusinessEmail = profile?.Email,
                InvoiceNumber = bill.BillNumber,
                InvoiceDate = bill.CreatedAt.AddHours(5.5),
                CustomerName = bill.CustomerName,
                PhoneNumber = bill.CustomerPhone,
                Subtotal = bill.Subtotal,
                TaxableAmount = bill.TaxableAmount,
                CgstAmount = bill.CgstAmount,
                SgstAmount = bill.SgstAmount,
                IgstAmount = bill.IgstAmount,
                GstAmount = bill.GstAmount,
                TotalAmount = bill.TotalAmount,
                PaymentMethod = bill.PaymentMethod,
                Items = itemResults
                    .Select(item => new GeneratedBillLineViewModel
                    {
                        ProductName = item.ProductName,
                        BatchNumber = item.BatchNumber,
                        Quantity = item.Quantity,
                        UnitPrice = item.UnitPrice,
                        Mrp = item.Mrp,
                        GstRate = item.GstRate,
                        GstAmount = item.GstAmount,
                        LineTotal = item.LineTotal
                    })
                    .ToList()
            };

            return View("Generated", generated);
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync(CancellationToken.None);
            _logger.LogError(
                ex,
                "Failed to generate bill for profile {ProfileId}.",
                profileId);

            TempData["BillingError"] =
                "The bill could not be generated. No stock or bill changes were saved.";
            return RedirectToAction(nameof(Create));
        }
    }

    private async Task<IActionResult> BillingErrorAsync(
        Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction transaction,
        string message)
    {
        await transaction.RollbackAsync();
        TempData["BillingError"] = message;
        return RedirectToAction(nameof(Create));
    }

    private async Task<string> GenerateUniqueBillNumberAsync(
        CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 5; attempt++)
        {
            var random = RandomNumberGenerator.GetInt32(100, 999);
            var candidate = $"PF-{DateTime.UtcNow:yyMMddHHmmss}-{random}";

            if (!await _dbContext.SalesBills.AnyAsync(
                    bill => bill.BillNumber == candidate,
                    cancellationToken))
                return candidate;
        }

        return $"PF-{DateTime.UtcNow:yyMMddHHmmssfff}-{RandomNumberGenerator.GetInt32(1000, 9999)}";
    }

    private static string? NormalizeOptionalText(string? value, int maxLength)
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

    private static string? NormalizePhoneNumber(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var normalized = new string(value.Where(char.IsDigit).ToArray());

        if (normalized.StartsWith("91", StringComparison.Ordinal) && normalized.Length == 12)
            normalized = normalized[2..];

        if (normalized.Length != 10 || normalized[0] is < '6' or > '9')
            return null;

        return normalized;
    }

    private static string NormalizePaymentMethod(string? paymentMethod) =>
        paymentMethod?.Trim().ToUpperInvariant() switch
        {
            "UPI" => "UPI",
            "UDHAAR" => "Udhaar",
            _ => "Cash"
        };

    private bool TryGetProfileId(out long profileId) =>
        long.TryParse(HttpContext.Session.GetString("ProfileId"), out profileId);
}
