using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PharmaFlow.Data;
using PharmaFlow.Filters;
using PharmaFlow.Models.ViewModels;

namespace PharmaFlow.Controllers;

[SessionAuthorize]
public sealed class PurchaseController : Controller
{
    private readonly ApplicationDbContext _dbContext;

    public PurchaseController(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
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

        var rows = await query
            .Select(invoice => new
            {
                invoice.DistributorName,
                Amount = invoice.TotalAmount ?? 0m
            })
            .ToListAsync(cancellationToken);

        var distributors = rows
            .GroupBy(
                row => row.DistributorName!,
                StringComparer.OrdinalIgnoreCase)
            .Select(group => new PurchaseDistributorViewModel
            {
                Name = group.Key,
                InvoiceCount = group.Count(),
                TotalAmount = group.Sum(row => row.Amount)
            })
            .OrderBy(group => group.Name)
            .ToList();

        var model = new PurchaseListViewModel
        {
            Period = normalizedPeriod,
            FromDate = from,
            ToDate = to,
            TotalPurchasedAmount = rows.Sum(row => row.Amount),
            InvoiceCount = rows.Count,
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

        var invoices = await _dbContext.InvoiceImports
            .AsNoTracking()
            .Include(invoice => invoice.Items)
            .Where(invoice =>
                invoice.ProfileId == profileId &&
                invoice.Status == "saved" &&
                invoice.DistributorName != null &&
                invoice.DistributorName.ToLower() == name.ToLower())
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
                Items = invoice.Items
                    .Where(item => item.ValidationStatus != "removed")
                    .OrderBy(item => item.RowNumber)
                    .Select(item => new PurchaseInvoiceItemViewModel
                    {
                        ProductName = item.ProductName,
                        BatchNumber = item.BatchNumber,
                        Quantity = item.Quantity ?? 0m,
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
