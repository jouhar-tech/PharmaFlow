using System.Data;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PharmaFlow.Data;
using PharmaFlow.Filters;
using PharmaFlow.Models;
using PharmaFlow.Models.ViewModels;

namespace PharmaFlow.Controllers;

[SessionAuthorize]
public sealed class BillingController : Controller
{
    private readonly ApplicationDbContext _dbContext;
    private readonly ILogger<BillingController> _logger;

    public BillingController(
        ApplicationDbContext dbContext,
        ILogger<BillingController> logger)
    {
        _dbContext = dbContext;
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

        var pattern = $"%{searchTerm.Replace("%", "\%").Replace("_", "\_")}%";

        var products = await _dbContext.Products
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
            .ToListAsync(cancellationToken);

        if (products.Count == 0)
            return Ok(Array.Empty<BillingSearchResultViewModel>());

        var productIds = products.Select(p => p.ProductId).ToList();

        var batches = await _dbContext.ProductBatches
            .AsNoTracking()
            .Where(b =>
                productIds.Contains(b.ProductId) &&
                b.IsActive &&
                !b.IsQuarantined &&
                b.QuantityOnHand > 0 &&
                b.ExpiryDate >= DateOnly.FromDateTime(DateTime.UtcNow))
            .OrderBy(b => b.ExpiryDate)
            .ThenBy(b => b.BatchNumber)
            .ToListAsync(cancellationToken);

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

        if (customerName is null && phoneNumber is null)
        {
            // Walk-in sale is allowed.
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

        var lines = requestedLines
            .GroupBy(line => line.BatchId)
            .Select(group => new BillingLineInput
            {
                BatchId = group.Key,
                Quantity = group.Sum(line => line.Quantity)
            })
            .ToList();

        if (lines.Any(line => line.BatchId <= 0 || line.Quantity <= 0 || line.Quantity > 999_999m))
        {
            TempData["BillingError"] = "One or more bill quantities are invalid.";
            return RedirectToAction(nameof(Create));
        }

        lines = lines.OrderBy(line => line.BatchId).ToList();

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
            var today = DateOnly.FromDateTime(DateTime.UtcNow);

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
            await transaction.RollbackAsync(cancellationToken);
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
