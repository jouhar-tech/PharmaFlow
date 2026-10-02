using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PharmaFlow.Data;
using PharmaFlow.Filters;
using PharmaFlow.Models;
using PharmaFlow.Models.ViewModels;
using System.Diagnostics;

namespace PharmaFlow.Controllers
{
    public class HomeController : Controller
    {
        private readonly ApplicationDbContext _dbContext;

        public HomeController(ApplicationDbContext dbContext)
        {
            _dbContext = dbContext;
        }

        [SessionAuthorize]
        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public async Task<IActionResult> Index(CancellationToken cancellationToken)
        {
            var profileIdValue = HttpContext.Session.GetString("ProfileId");

            var expiringSoonCount = 0;
            var lowStockCount = 0;
            decimal expiryAtRiskValue = 0m;
            decimal slowMovingStockValue = 0m;
            decimal todaySalesAmount = 0m;

            if (long.TryParse(profileIdValue, out var profileId))
            {
                var today = DateOnly.FromDateTime(DateTime.UtcNow);
                var ninetyDaysFromToday = today.AddDays(90);

                var indiaTimeZone = TimeZoneInfo.FindSystemTimeZoneById(
                    OperatingSystem.IsWindows() ? "India Standard Time" : "Asia/Kolkata");
                var indiaNow = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, indiaTimeZone);
                var indiaDate = DateOnly.FromDateTime(indiaNow);
                var indiaStartUtc = TimeZoneInfo.ConvertTimeToUtc(
                    indiaDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified),
                    indiaTimeZone);
                var indiaEndUtc = TimeZoneInfo.ConvertTimeToUtc(
                    indiaDate.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified),
                    indiaTimeZone);

                expiringSoonCount = await _dbContext.ProductBatches
                    .AsNoTracking()
                    .CountAsync(batch =>
                        batch.Product.ProfileId == profileId &&
                        batch.Product.IsActive &&
                        batch.IsActive &&
                        !batch.IsQuarantined &&
                        batch.QuantityOnHand > 0 &&
                        batch.ExpiryDate >= today &&
                        batch.ExpiryDate <= ninetyDaysFromToday,
                        cancellationToken);

                // Keep the dashboard count identical to the Low Stock page:
                // active, usable batches with quantity below 3.
                lowStockCount = await _dbContext.ProductBatches
                    .AsNoTracking()
                    .CountAsync(batch =>
                        batch.Product.ProfileId == profileId &&
                        batch.Product.IsActive &&
                        batch.IsActive &&
                        !batch.IsQuarantined &&
                        batch.QuantityOnHand > 0 &&
                        batch.QuantityOnHand < 3m,
                        cancellationToken);

                expiryAtRiskValue = await _dbContext.ProductBatches
                    .AsNoTracking()
                    .Where(batch =>
                        batch.Product.ProfileId == profileId &&
                        batch.Product.IsActive &&
                        batch.IsActive &&
                        !batch.IsQuarantined &&
                        batch.QuantityOnHand > 0 &&
                        batch.ExpiryDate >= today &&
                        batch.ExpiryDate <= ninetyDaysFromToday)
                    .SumAsync(batch => batch.QuantityOnHand * batch.PurchaseUnitPrice, cancellationToken);

                // Until detailed sales-velocity history is added, classify stock held for more
                // than 90 days as slow-moving inventory. This keeps the dashboard value data-driven
                // while billing history remains separate from stock-aging analysis.
                var slowMovingCutoffDate = today.AddDays(-90);
                var slowMovingCutoffUtc = DateTime.SpecifyKind(
                    slowMovingCutoffDate.ToDateTime(TimeOnly.MinValue),
                    DateTimeKind.Utc);

                slowMovingStockValue = await _dbContext.ProductBatches
                    .AsNoTracking()
                    .Where(batch =>
                        batch.Product.ProfileId == profileId &&
                        batch.Product.IsActive &&
                        batch.IsActive &&
                        !batch.IsQuarantined &&
                        batch.QuantityOnHand > 0 &&
                        batch.CreatedAt <= slowMovingCutoffUtc)
                    .SumAsync(batch => batch.QuantityOnHand * batch.PurchaseUnitPrice, cancellationToken);

                todaySalesAmount = await _dbContext.SalesBills
                    .AsNoTracking()
                    .Where(bill =>
                        bill.ProfileId == profileId &&
                        bill.Status == "Completed" &&
                        bill.CreatedAt >= indiaStartUtc &&
                        bill.CreatedAt < indiaEndUtc)
                    .SumAsync(bill => bill.TotalAmount, cancellationToken);
            }

            var dashboard = new DashboardViewModel
            {
                ProductsExpiringSoonCount = expiringSoonCount,
                LowStockCount = lowStockCount,
                ExpiryAtRiskValue = expiryAtRiskValue,
                SlowMovingStockValue = slowMovingStockValue,
                TodaySalesAmount = todaySalesAmount
            };

            return View(dashboard);
        }

        [HttpGet]
        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public IActionResult Ping()
        {
            return long.TryParse(HttpContext.Session.GetString("ProfileId"), out _)
                ? Ok()
                : Unauthorized();
        }

        public IActionResult Privacy()
        {
            return View();
        }

        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public IActionResult Error()
        {
            return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
        }
    }
}
