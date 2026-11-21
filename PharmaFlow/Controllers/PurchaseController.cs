using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PharmaFlow.Data;
using PharmaFlow.Filters;
using PharmaFlow.Models.ViewModels;
using PharmaFlow.Services;

namespace PharmaFlow.Controllers;

[SessionAuthorize]
public sealed class PurchaseController : Controller
{
    private readonly ApplicationDbContext _dbContext;
    private readonly ISupabaseStorageService _storageService;
    private readonly ILogger<PurchaseController> _logger;

    public PurchaseController(
        ApplicationDbContext dbContext,
        ISupabaseStorageService storageService,
        ILogger<PurchaseController> logger)
    {
        _dbContext = dbContext;
        _storageService = storageService;
        _logger = logger;
    }

    [HttpGet]
    public async Task<IActionResult> Index(
        string? period,
        DateOnly? from,
        DateOnly? to,
        CancellationToken cancellationToken)
    {
        if (!TryGetProfileId(out var profileId))
            return RedirectToAction("Login", "Account");

        var normalizedPeriod = NormalizePeriod(period);

        if (normalizedPeriod == "custom")
        {
            if (!from.HasValue || !to.HasValue)
            {
                normalizedPeriod = "all";
                from = null;
                to = null;
            }
            else if (from.Value > to.Value)
            {
                (from, to) = (to.Value, from.Value);
            }
        }
        else
        {
            from = null;
            to = null;
        }

        var today = DateOnly.FromDateTime(
        DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(5.5)).DateTime);
        var query = _dbContext.InvoiceImports
            .AsNoTracking()
            .Where(invoice =>
                invoice.ProfileId == profileId &&
                invoice.Status == "saved" &&
                invoice.DistributorName != null &&
                invoice.DistributorName != "");

        if (normalizedPeriod == "last7")
        {
            var start = today.AddDays(-6);
            query = query.Where(invoice =>
                invoice.InvoiceDate.HasValue &&
                invoice.InvoiceDate.Value >= start &&
                invoice.InvoiceDate.Value <= today);
        }
        else if (normalizedPeriod == "month")
        {
            var start = today.AddMonths(-1);
            query = query.Where(invoice =>
                invoice.InvoiceDate.HasValue &&
                invoice.InvoiceDate.Value >= start &&
                invoice.InvoiceDate.Value <= today);
        }
        else if (normalizedPeriod == "custom" && from.HasValue && to.HasValue)
        {
            var fromDate = from.Value;
            var toDate = to.Value;

            query = query.Where(invoice =>
                invoice.InvoiceDate.HasValue &&
                invoice.InvoiceDate.Value >= fromDate &&
                invoice.InvoiceDate.Value <= toDate);
        }

        var distributors = await query
            .GroupBy(invoice => invoice.DistributorName!)
            .Select(group => new PurchaseDistributorViewModel
            {
                Name = group.Key,
                InvoiceCount = group.Count(),
                TotalAmount = group.Sum(invoice => invoice.TotalAmount ?? 0m)
            })
            .OrderBy(group => group.Name)
            .ToListAsync(cancellationToken);

        var model = new PurchaseListViewModel
        {
            Period = normalizedPeriod,
            FromDate = from,
            ToDate = to,
            TotalPurchasedAmount = distributors.Sum(row => row.TotalAmount),
            InvoiceCount = distributors.Sum(row => row.InvoiceCount),
            Distributors = distributors
        };

        ViewData["Title"] = "Purchase";
        return View(model);
    }

    [HttpGet]
    public async Task<IActionResult> Distributor(
        string name,
        CancellationToken cancellationToken)
    {
        if (!TryGetProfileId(out var profileId))
            return RedirectToAction("Login", "Account");

        name = NormalizeDistributor(name);

        if (string.IsNullOrWhiteSpace(name))
            return NotFound();

        var safeName = name
            .Replace("\\", "\\\\")
            .Replace("%", "\\%")
            .Replace("_", "\\_");

        var invoices = await _dbContext.InvoiceImports
            .AsNoTracking()
            .Where(invoice =>
                invoice.ProfileId == profileId &&
                invoice.Status == "saved" &&
                invoice.DistributorName != null &&
                EF.Functions.ILike(invoice.DistributorName, safeName, "\\"))
            .OrderByDescending(invoice => invoice.InvoiceDate)
            .ThenByDescending(invoice => invoice.CreatedAt)
            .Select(invoice => new PurchaseInvoiceViewModel
            {
                ImportId = invoice.ImportId,
                InvoiceNumber = string.IsNullOrWhiteSpace(invoice.InvoiceNumber)
                    ? invoice.OriginalFileName
                    : invoice.InvoiceNumber!,
                InvoiceDate = invoice.InvoiceDate,
                TotalAmount = invoice.TotalAmount ?? 0m,
                HasScannedFile = !string.IsNullOrWhiteSpace(invoice.StoragePath),
                IsPdf = invoice.StorageContentType == "application/pdf",
                Items = invoice.Items
                    .Where(item => item.ValidationStatus != "removed")
                    .OrderBy(item => item.RowNumber)
                    .Select(item => new PurchaseInvoiceItemViewModel
                    {
                        ProductName = item.ProductName,
                        BatchNumber = item.BatchNumber,
                        Quantity = item.Quantity ?? 0m,
                        PurchaseUnitPrice = item.PurchaseUnitPrice,
                        Mrp = item.Mrp,
                        ExpiryDate = item.ExpiryDate
                    })
                    .ToList()
            })
            .ToListAsync(cancellationToken);

        if (invoices.Count == 0)
            return NotFound();

        ViewData["Title"] = name;

        return View(new PurchaseDistributorDetailsViewModel
        {
            DistributorName = name,
            TotalAmount = invoices.Sum(invoice => invoice.TotalAmount),
            InvoiceCount = invoices.Count,
            Invoices = invoices
        });
    }

    [HttpGet]
    public async Task<IActionResult> InvoiceFile(
        long id,
        CancellationToken cancellationToken)
    {
        if (!TryGetProfileId(out var profileId))
            return Unauthorized();

        var invoiceFile = await _dbContext.InvoiceImports
            .AsNoTracking()
            .Where(invoice =>
                invoice.ImportId == id &&
                invoice.ProfileId == profileId &&
                invoice.Status == "saved")
            .Select(invoice => new
            {
                invoice.StoragePath,
                invoice.StorageContentType
            })
            .SingleOrDefaultAsync(cancellationToken);

        if (invoiceFile is null || string.IsNullOrWhiteSpace(invoiceFile.StoragePath))
            return NotFound();

        try
        {
            var storedObject = await _storageService.DownloadAsync(
                invoiceFile.StoragePath,
                invoiceFile.StorageContentType,
                cancellationToken);

            if (storedObject is null)
                return NotFound();

            Response.Headers.CacheControl = "private, no-store";
            Response.Headers["X-Content-Type-Options"] = "nosniff";

            var isPdf = string.Equals(
                storedObject.ContentType,
                "application/pdf",
                StringComparison.OrdinalIgnoreCase);

            return File(
                storedObject.Content,
                storedObject.ContentType,
                enableRangeProcessing: isPdf);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Unable to retrieve scanned invoice {ImportId} for profile {ProfileId}.",
                id,
                profileId);

            return StatusCode(StatusCodes.Status502BadGateway);
        }
    }

    private static string NormalizePeriod(string? period) =>
        period?.Trim().ToLowerInvariant() switch
        {
            "last7" => "last7",
            "month" => "month",
            "custom" => "custom",
            _ => "all"
        };

    private static string NormalizeDistributor(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return string.Empty;

        var normalized = string.Join(
            " ",
            name.Trim().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

        return normalized.Length <= 200
            ? normalized
            : normalized[..200].Trim();
    }

    private bool TryGetProfileId(out long profileId) =>
        long.TryParse(HttpContext.Session.GetString("ProfileId"), out profileId);
}
