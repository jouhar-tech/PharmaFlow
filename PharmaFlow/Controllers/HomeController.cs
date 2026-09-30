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

            if (long.TryParse(profileIdValue, out var profileId))
            {
                var today = DateOnly.FromDateTime(DateTime.UtcNow);
                var ninetyDaysFromToday = today.AddDays(90);

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

                // Until billing/sales history is available, classify stock held for more than
                // 90 days as slow-moving inventory. This keeps the dashboard value data-driven
                // without pretending we have sales velocity data.
                var slowMovingCutoff = today.AddDays(-90);
                slowMovingStockValue = await _dbContext.ProductBatches
                    .AsNoTracking()
                    .Where(batch =>
                        batch.Product.ProfileId == profileId &&
                        batch.Product.IsActive &&
                        batch.IsActive &&
                        !batch.IsQuarantined &&
                        batch.QuantityOnHand > 0 &&
                        batch.CreatedAt <= slowMovingCutoff.ToDateTime(TimeOnly.MinValue))
                    .SumAsync(batch => batch.QuantityOnHand * batch.PurchaseUnitPrice, cancellationToken);
            }

            var dashboard = new DashboardViewModel
            {
                ProductsExpiringSoonCount = expiringSoonCount,
                LowStockCount = lowStockCount,
                ExpiryAtRiskValue = expiryAtRiskValue,
                SlowMovingStockValue = slowMovingStockValue
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
