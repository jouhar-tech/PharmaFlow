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

                // Count products whose total usable stock is below their configured reorder level.
                // Products with a zero reorder level are excluded because no low-stock threshold is configured.
                lowStockCount = await _dbContext.Products
                    .AsNoTracking()
                    .Where(product =>
                        product.ProfileId == profileId &&
                        product.IsActive &&
                        product.ReorderLevel > 0m)
                    .Select(product => new
                    {
                        product.ReorderLevel,
                        QuantityOnHand = product.Batches
                            .Where(batch => batch.IsActive && !batch.IsQuarantined)
                            .Select(batch => (decimal?)batch.QuantityOnHand)
                            .Sum() ?? 0m
                    })
                    .CountAsync(stock => stock.QuantityOnHand < stock.ReorderLevel, cancellationToken);
            }

            var dashboard = new DashboardViewModel
            {
                ProductsExpiringSoonCount = expiringSoonCount,
                LowStockCount = lowStockCount
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
