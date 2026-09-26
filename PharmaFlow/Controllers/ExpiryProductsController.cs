using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PharmaFlow.Data;
using PharmaFlow.Filters;
using PharmaFlow.Models.ViewModels;

namespace PharmaFlow.Controllers;

[SessionAuthorize]
public sealed class ExpiryProductsController : Controller
{
    private readonly ApplicationDbContext _dbContext;

    public ExpiryProductsController(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        var data = await LoadExpiryDataAsync(cancellationToken);

        return View(new ExpiringProductsViewModel
        {
            CriticalCount = data.CriticalCount,
            UpcomingCount = data.UpcomingCount,
            AtRiskValue = data.AtRiskValue,
            ViewMode = "overview"
        });
    }

    [HttpGet]
    public async Task<IActionResult> Critical(CancellationToken cancellationToken)
    {
        var data = await LoadExpiryDataAsync(cancellationToken);

        return View("Products", new ExpiringProductsViewModel
        {
            CriticalCount = data.CriticalCount,
            UpcomingCount = data.UpcomingCount,
            AtRiskValue = data.AtRiskValue,
            Products = data.CriticalProducts,
            ViewMode = "critical"
        });
    }

    [HttpGet]
    public async Task<IActionResult> Upcoming(CancellationToken cancellationToken)
    {
        var data = await LoadExpiryDataAsync(cancellationToken);

        return View("Products", new ExpiringProductsViewModel
        {
            CriticalCount = data.CriticalCount,
            UpcomingCount = data.UpcomingCount,
            AtRiskValue = data.AtRiskValue,
            Products = data.UpcomingProducts,
            ViewMode = "upcoming"
        });
    }

    [HttpGet]
    public async Task<IActionResult> AtRisk(CancellationToken cancellationToken)
    {
        var data = await LoadExpiryDataAsync(cancellationToken);

        return View("Products", new ExpiringProductsViewModel
        {
            CriticalCount = data.CriticalCount,
            UpcomingCount = data.UpcomingCount,
            AtRiskValue = data.AtRiskValue,
            Products = data.AtRiskProducts,
            ViewMode = "at-risk"
        });
    }

    private async Task<ExpiryData> LoadExpiryDataAsync(CancellationToken cancellationToken)
    {
        var profileIdValue = HttpContext.Session.GetString("ProfileId");
        if (!long.TryParse(profileIdValue, out var profileId))
            return new ExpiryData();

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var ninetyDaysFromToday = today.AddDays(90);

        var rows = await _dbContext.ProductBatches
            .AsNoTracking()
            .Where(batch =>
                batch.Product.ProfileId == profileId &&
                batch.Product.IsActive &&
                batch.IsActive &&
                !batch.IsQuarantined &&
                batch.QuantityOnHand > 0 &&
                batch.ExpiryDate >= today &&
                batch.ExpiryDate <= ninetyDaysFromToday)
            .OrderBy(batch => batch.ExpiryDate)
            .Select(batch => new
            {
                ProductId = batch.ProductId,
                BatchId = batch.BatchId,
                ProductName = batch.Product.ProductName,
                BatchNumber = batch.BatchNumber,
                ExpiryDate = batch.ExpiryDate,
                Quantity = batch.QuantityOnHand,
                PurchaseUnitPrice = batch.PurchaseUnitPrice
            })
            .ToListAsync(cancellationToken);

        var expiryItems = rows
            .Select(row => new ExpiryProductItemViewModel
            {
                ProductId = row.ProductId,
                BatchId = row.BatchId,
                ProductName = row.ProductName,
                BatchNumber = row.BatchNumber,
                ExpiryDate = row.ExpiryDate,
                DaysLeft = row.ExpiryDate.DayNumber - today.DayNumber,
                Quantity = row.Quantity,
                PurchaseUnitPrice = row.PurchaseUnitPrice,
                TotalValue = row.Quantity * row.PurchaseUnitPrice
            })
            .ToList();

        var critical = expiryItems
            .Where(row => row.DaysLeft >= 0 && row.DaysLeft <= 30)
            .ToList();

        var upcoming = expiryItems
            .Where(row => row.DaysLeft > 30 && row.DaysLeft <= 90)
            .ToList();

        return new ExpiryData
        {
            CriticalCount = critical.Count,
            UpcomingCount = upcoming.Count,
            AtRiskValue = expiryItems.Sum(row => row.TotalValue),
            CriticalProducts = critical,
            UpcomingProducts = upcoming,
            AtRiskProducts = expiryItems
        };
    }

    private sealed class ExpiryData
    {
        public int CriticalCount { get; init; }
        public int UpcomingCount { get; init; }
        public decimal AtRiskValue { get; init; }
        public IReadOnlyList<ExpiryProductItemViewModel> CriticalProducts { get; init; } = Array.Empty<ExpiryProductItemViewModel>();
        public IReadOnlyList<ExpiryProductItemViewModel> UpcomingProducts { get; init; } = Array.Empty<ExpiryProductItemViewModel>();
        public IReadOnlyList<ExpiryProductItemViewModel> AtRiskProducts { get; init; } = Array.Empty<ExpiryProductItemViewModel>();
    }
}
