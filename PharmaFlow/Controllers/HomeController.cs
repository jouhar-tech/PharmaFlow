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
                var indiaTimeZone = TimeZoneInfo.FindSystemTimeZoneById(
                    OperatingSystem.IsWindows() ? "India Standard Time" : "Asia/Kolkata");
                var indiaNow = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, indiaTimeZone);
                var today = DateOnly.FromDateTime(indiaNow);
                var ninetyDaysFromToday = today.AddDays(90);
                var indiaStartUtc = TimeZoneInfo.ConvertTimeToUtc(
                    today.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified),
                    indiaTimeZone);
                var indiaEndUtc = TimeZoneInfo.ConvertTimeToUtc(
                    today.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified),
                    indiaTimeZone);
                var slowMovingCutoffDate = today.AddDays(-90);
                var slowMovingCutoffUtc = TimeZoneInfo.ConvertTimeToUtc(
                    slowMovingCutoffDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified),
                    indiaTimeZone);

                var stockMetrics = await _dbContext.ProductBatches
                    .AsNoTracking()
                    .Where(batch =>
                        batch.Product.ProfileId == profileId &&
                        batch.Product.IsActive &&
                        batch.IsActive &&
                        !batch.IsQuarantined &&
                        batch.QuantityOnHand > 0)
                    .GroupBy(_ => 1)
                    .Select(group => new
                    {
                        ExpiringSoonCount = group.Count(batch =>
                            batch.ExpiryDate >= today &&
                            batch.ExpiryDate <= ninetyDaysFromToday),
                        LowStockCount = group.Count(batch =>
                            batch.QuantityOnHand < 3m),
                        ExpiryAtRiskValue = group
                            .Where(batch =>
                                batch.ExpiryDate >= today &&
                                batch.ExpiryDate <= ninetyDaysFromToday)
                            .Sum(batch => (decimal?)(
                                batch.QuantityOnHand * batch.PurchaseUnitPrice)) ?? 0m,
                        SlowMovingStockValue = group
                            .Where(batch => batch.CreatedAt <= slowMovingCutoffUtc)
                            .Sum(batch => (decimal?)(
                                batch.QuantityOnHand * batch.PurchaseUnitPrice)) ?? 0m
                    })
                    .SingleOrDefaultAsync(cancellationToken);

                expiringSoonCount = stockMetrics?.ExpiringSoonCount ?? 0;
                lowStockCount = stockMetrics?.LowStockCount ?? 0;
                expiryAtRiskValue = stockMetrics?.ExpiryAtRiskValue ?? 0m;
                slowMovingStockValue = stockMetrics?.SlowMovingStockValue ?? 0m;

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
