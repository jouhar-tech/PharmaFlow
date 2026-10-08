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
            var slowMovingStockCount = 0;
            decimal slowMovingStockValue = 0m;
            decimal todaySalesAmount = 0m;
            decimal customerOutstandingAmount = 0m;
            var customersWithOutstanding = 0;
            var remindersDueCount = 0;

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
                var slowMovingCutoffDate = today.AddMonths(-1);
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
                        SlowMovingStockCount = group
                            .Where(batch =>
                                !_dbContext.SalesBillItems.Any(item =>
                                    item.ProductId == batch.ProductId &&
                                    item.Bill.ProfileId == profileId &&
                                    item.Bill.Status == "Completed" &&
                                    item.Bill.CreatedAt > slowMovingCutoffUtc))
                            .Select(batch => batch.ProductId)
                            .Distinct()
                            .Count(),
                        SlowMovingStockValue = group
                            .Where(batch =>
                                !_dbContext.SalesBillItems.Any(item =>
                                    item.ProductId == batch.ProductId &&
                                    item.Bill.ProfileId == profileId &&
                                    item.Bill.Status == "Completed" &&
                                    item.Bill.CreatedAt > slowMovingCutoffUtc))
                            .Sum(batch => (decimal?)(
                                batch.QuantityOnHand * batch.PurchaseUnitPrice)) ?? 0m
                    })
                    .SingleOrDefaultAsync(cancellationToken);

                expiringSoonCount = stockMetrics?.ExpiringSoonCount ?? 0;
                lowStockCount = stockMetrics?.LowStockCount ?? 0;
                expiryAtRiskValue = stockMetrics?.ExpiryAtRiskValue ?? 0m;
                slowMovingStockCount = stockMetrics?.SlowMovingStockCount ?? 0;
                slowMovingStockValue = stockMetrics?.SlowMovingStockValue ?? 0m;

                todaySalesAmount = await _dbContext.SalesBills
                    .AsNoTracking()
                    .Where(bill =>
                        bill.ProfileId == profileId &&
                        bill.Status == "Completed" &&
                        bill.CreatedAt >= indiaStartUtc &&
                        bill.CreatedAt < indiaEndUtc)
                    .SumAsync(bill => bill.TotalAmount, cancellationToken);

                var customerBalances = await _dbContext.CustomerLedgerEntries
                    .AsNoTracking()
                    .Where(entry => entry.ProfileId == profileId)
                    .GroupBy(entry => entry.CustomerId)
                    .Select(group => group.Sum(entry => entry.BalanceChange))
                    .ToListAsync(cancellationToken);

                customerOutstandingAmount = customerBalances.Where(balance => balance > 0m).Sum();
                customersWithOutstanding = customerBalances.Count(balance => balance > 0m);

                remindersDueCount = await _dbContext.CustomerReminders
                    .AsNoTracking()
                    .CountAsync(reminder =>
                        reminder.ProfileId == profileId &&
                        (reminder.Status == "Pending" || reminder.Status == "Ordered") &&
                        reminder.ReminderDate.HasValue &&
                        reminder.ReminderDate.Value <= today,
                        cancellationToken);

            }

            var dashboard = new DashboardViewModel
            {
                ProductsExpiringSoonCount = expiringSoonCount,
                LowStockCount = lowStockCount,
                ExpiryAtRiskValue = expiryAtRiskValue,
                SlowMovingStockCount = slowMovingStockCount,
                SlowMovingStockValue = slowMovingStockValue,
                TodaySalesAmount = todaySalesAmount,
                CustomerOutstandingAmount = customerOutstandingAmount,
                CustomersWithOutstanding = customersWithOutstanding,
                RemindersDueCount = remindersDueCount
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
