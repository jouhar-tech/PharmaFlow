using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Net.Http.Headers;
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
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly ILogger<HomeController> _logger;

        public HomeController(
            ApplicationDbContext dbContext,
            IHttpClientFactory httpClientFactory,
            ILogger<HomeController> logger)
        {
            _dbContext = dbContext;
            _httpClientFactory = httpClientFactory;
            _logger = logger;
        }

        [SessionAuthorize]
        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public async Task<IActionResult> Index(CancellationToken cancellationToken)
        {
            var profileIdValue = HttpContext.Session.GetString("ProfileId");

            if (HttpContext.Session.GetString("GoogleProfileSetupRequired") == "true")
                return RedirectToAction("Setup", "Business");

            var expiringSoonCount = 0;
            var lowStockCount = 0;
            decimal expiryAtRiskValue = 0m;
            var slowMovingStockCount = 0;
            decimal slowMovingStockValue = 0m;
            decimal todaySalesAmount = 0m;
            decimal customerOutstandingAmount = 0m;
            var customersWithOutstanding = 0;
            var remindersDueCount = 0;
            string? profileFullName = null;
            string? profilePhotoUrl = null;
            string? dashboardBackgroundUrl = null;

            if (long.TryParse(profileIdValue, out var profileId))
            {
                var profileIdentity = await _dbContext.Profiles
                    .AsNoTracking()
                    .Where(profile => profile.Id == profileId)
                    .Select(profile => new
                    {
                        profile.FullName,
                        profile.ProfilePhotoUrl,
                        profile.DashboardBackgroundUrl,
                        profile.BusinessName
                    })
                    .SingleOrDefaultAsync(cancellationToken);

                if (profileIdentity is null)
                    return RedirectToAction("Login", "Account");

                if (string.IsNullOrWhiteSpace(profileIdentity.BusinessName))
                    return RedirectToAction("Setup", "Business");

                profileFullName = profileIdentity.FullName;
                profilePhotoUrl = profileIdentity.ProfilePhotoUrl;
                dashboardBackgroundUrl = profileIdentity.DashboardBackgroundUrl;

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
                                batch.QuantityOnHand *
                                (batch.PurchaseUnitPrice > 0m
                                    ? batch.PurchaseUnitPrice
                                    : batch.SellingUnitPrice))) ?? 0m,
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
                ProfileFullName = profileFullName,
                ProfilePhotoUrl = profilePhotoUrl,
                DashboardBackgroundUrl = dashboardBackgroundUrl,
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


        [SessionAuthorize]
        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequestSizeLimit(6 * 1024 * 1024)]
        public async Task<IActionResult> UploadDashboardBackground(
            IFormFile? backgroundImage,
            CancellationToken cancellationToken)
        {
            if (!long.TryParse(HttpContext.Session.GetString("ProfileId"), out var profileId))
                return RedirectToAction("Login", "Account");

            var profile = await _dbContext.Profiles
                .SingleOrDefaultAsync(
                    item => item.Id == profileId && item.ActiveStatus == 1,
                    cancellationToken);

            if (profile is null)
                return RedirectToAction("Login", "Account");

            if (backgroundImage is null || backgroundImage.Length == 0)
            {
                TempData["DashboardBackgroundError"] = "Choose an image first.";
                return RedirectToAction(nameof(Index));
            }

            const long maxImageBytes = 5 * 1024 * 1024;
            if (backgroundImage.Length > maxImageBytes)
            {
                TempData["DashboardBackgroundError"] = "Choose an image up to 5 MB.";
                return RedirectToAction(nameof(Index));
            }

            var extension = Path.GetExtension(backgroundImage.FileName).ToLowerInvariant();
            var expectedContentType = extension switch
            {
                ".jpg" or ".jpeg" => "image/jpeg",
                ".png" => "image/png",
                ".webp" => "image/webp",
                _ => null
            };

            if (expectedContentType is null ||
                !string.Equals(backgroundImage.ContentType, expectedContentType, StringComparison.OrdinalIgnoreCase) ||
                !HasValidDashboardImageSignature(backgroundImage, extension))
            {
                TempData["DashboardBackgroundError"] = "Use a valid JPG, PNG or WebP image.";
                return RedirectToAction(nameof(Index));
            }

            var accessToken = HttpContext.Session.GetString("SupabaseAccessToken");
            var configuration = HttpContext.RequestServices.GetRequiredService<IConfiguration>();
            var baseUrl = configuration["Supabase:Url"]?.TrimEnd('/');
            var anonKey = configuration["Supabase:AnonKey"];

            if (string.IsNullOrWhiteSpace(accessToken) ||
                string.IsNullOrWhiteSpace(baseUrl) ||
                string.IsNullOrWhiteSpace(anonKey))
            {
                TempData["DashboardBackgroundError"] = "Image storage is not configured. Please try again later.";
                return RedirectToAction(nameof(Index));
            }

            var previousImageUrl = profile.DashboardBackgroundUrl;
            var objectPath = $"{profile.UserId:D}/dashboard-backgrounds/{profile.Id}/{Guid.NewGuid():N}{extension}";
            var publicImageUrl = $"{baseUrl}/storage/v1/object/public/business-backgrounds/{objectPath}";

            try
            {
                var client = _httpClientFactory.CreateClient();
                using var stream = backgroundImage.OpenReadStream();
                using var request = new HttpRequestMessage(
                    HttpMethod.Post,
                    $"{baseUrl}/storage/v1/object/business-backgrounds/{objectPath}");
                request.Headers.Add("apikey", anonKey);
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
                request.Headers.Add("x-upsert", "false");
                request.Content = new StreamContent(stream);
                request.Content.Headers.ContentType = new MediaTypeHeaderValue(expectedContentType);

                using var response = await client.SendAsync(request, cancellationToken);
                if (!response.IsSuccessStatusCode)
                {
                    var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
                    _logger.LogWarning(
                        "Dashboard background upload failed for profile {ProfileId}. HTTP {StatusCode}: {Response}",
                        profileId,
                        (int)response.StatusCode,
                        responseBody.Length > 800 ? responseBody[..800] : responseBody);

                    TempData["DashboardBackgroundError"] = "The background photo could not be uploaded. Please try again.";
                    return RedirectToAction(nameof(Index));
                }

                profile.DashboardBackgroundUrl = publicImageUrl;
                await _dbContext.SaveChangesAsync(cancellationToken);
            }
            catch (Exception ex)
            {
                profile.DashboardBackgroundUrl = previousImageUrl;
                _logger.LogError(ex, "Failed to save dashboard background for profile {ProfileId}.", profileId);
                await DeleteDashboardBackgroundStorageObjectAsync(publicImageUrl, profile.UserId, CancellationToken.None);
                TempData["DashboardBackgroundError"] = "The background photo could not be saved. Please try again.";
                return RedirectToAction(nameof(Index));
            }

            if (!string.IsNullOrWhiteSpace(previousImageUrl))
                await DeleteDashboardBackgroundStorageObjectAsync(previousImageUrl, profile.UserId, cancellationToken);

            TempData["DashboardBackgroundMessage"] = "Shop background photo updated.";
            return RedirectToAction(nameof(Index));
        }

        [SessionAuthorize]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RemoveDashboardBackground(CancellationToken cancellationToken)
        {
            if (!long.TryParse(HttpContext.Session.GetString("ProfileId"), out var profileId))
                return RedirectToAction("Login", "Account");

            var profile = await _dbContext.Profiles
                .SingleOrDefaultAsync(
                    item => item.Id == profileId && item.ActiveStatus == 1,
                    cancellationToken);

            if (profile is null)
                return RedirectToAction("Login", "Account");

            var previousImageUrl = profile.DashboardBackgroundUrl;
            if (string.IsNullOrWhiteSpace(previousImageUrl))
                return RedirectToAction(nameof(Index));

            profile.DashboardBackgroundUrl = null;
            try
            {
                await _dbContext.SaveChangesAsync(cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to remove dashboard background for profile {ProfileId}.", profileId);
                TempData["DashboardBackgroundError"] = "The background photo could not be removed. Please try again.";
                return RedirectToAction(nameof(Index));
            }

            await DeleteDashboardBackgroundStorageObjectAsync(
                previousImageUrl,
                profile.UserId,
                cancellationToken);

            TempData["DashboardBackgroundMessage"] = "Default dashboard background restored.";
            return RedirectToAction(nameof(Index));
        }

        private async Task DeleteDashboardBackgroundStorageObjectAsync(
            string? publicImageUrl,
            Guid userId,
            CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(publicImageUrl))
                return;

            var accessToken = HttpContext.Session.GetString("SupabaseAccessToken");
            var configuration = HttpContext.RequestServices.GetRequiredService<IConfiguration>();
            var baseUrl = configuration["Supabase:Url"]?.TrimEnd('/');
            var anonKey = configuration["Supabase:AnonKey"];

            if (string.IsNullOrWhiteSpace(accessToken) ||
                string.IsNullOrWhiteSpace(baseUrl) ||
                string.IsNullOrWhiteSpace(anonKey))
                return;

            var publicPrefix = $"{baseUrl}/storage/v1/object/public/business-backgrounds/{userId:D}/dashboard-backgrounds/";
            if (!publicImageUrl.StartsWith(publicPrefix, StringComparison.OrdinalIgnoreCase))
                return;

            var objectPath = Uri.UnescapeDataString(publicImageUrl[publicPrefix.Length..]);
            if (string.IsNullOrWhiteSpace(objectPath) ||
                objectPath.Contains("..", StringComparison.Ordinal) ||
                objectPath.StartsWith("/", StringComparison.Ordinal))
                return;

            try
            {
                var client = _httpClientFactory.CreateClient();
                using var request = new HttpRequestMessage(
                    HttpMethod.Delete,
                    $"{baseUrl}/storage/v1/object/business-backgrounds/{userId:D}/dashboard-backgrounds/{objectPath}");
                request.Headers.Add("apikey", anonKey);
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

                using var response = await client.SendAsync(request, cancellationToken);
                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogWarning(
                        "Could not delete old dashboard background for user {UserId}. HTTP {StatusCode}.",
                        userId,
                        (int)response.StatusCode);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not delete old dashboard background for user {UserId}.", userId);
            }
        }

        private static bool HasValidDashboardImageSignature(IFormFile file, string extension)
        {
            using var stream = file.OpenReadStream();
            var header = new byte[12];
            var bytesRead = stream.Read(header, 0, header.Length);
            if (bytesRead < header.Length)
                return false;

            return extension switch
            {
                ".jpg" or ".jpeg" =>
                    header[0] == 0xFF && header[1] == 0xD8 && header[2] == 0xFF,
                ".png" =>
                    header[0] == 0x89 && header[1] == 0x50 && header[2] == 0x4E &&
                    header[3] == 0x47 && header[4] == 0x0D && header[5] == 0x0A &&
                    header[6] == 0x1A && header[7] == 0x0A,
                ".webp" =>
                    header[0] == (byte)'R' && header[1] == (byte)'I' &&
                    header[2] == (byte)'F' && header[3] == (byte)'F' &&
                    header[8] == (byte)'W' && header[9] == (byte)'E' &&
                    header[10] == (byte)'B' && header[11] == (byte)'P',
                _ => false
            };
        }

        [AllowAnonymous]
        [HttpGet]
        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public IActionResult Ping()
        {
            // This endpoint only verifies that the web application can respond.
            // Connectivity must not depend on an owner ProfileId because Admin
            // sessions are authenticated separately and do not have that key.
            return Ok(new { reachable = true });
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
