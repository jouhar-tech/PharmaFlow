using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Net.Http.Headers;
using PharmaFlow.Data;
using PharmaFlow.Filters;
using PharmaFlow.Models;
using PharmaFlow.Models.ViewModels;

namespace PharmaFlow.Controllers;

[SessionAuthorize]
public sealed class ProfileController : Controller
{
    private readonly ApplicationDbContext _dbContext;
    private readonly ILogger<ProfileController> _logger;
    private readonly IHttpClientFactory _httpClientFactory;

    public ProfileController(
        ApplicationDbContext dbContext,
        ILogger<ProfileController> logger,
        IHttpClientFactory httpClientFactory)
    {
        _dbContext = dbContext;
        _logger = logger;
        _httpClientFactory = httpClientFactory;
    }

    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        var profile = await GetCurrentProfileAsync(cancellationToken);
        if (profile is null)
            return RedirectToAction("Login", "Account");

        return View(new ProfileViewModel
        {
            BusinessName = profile.BusinessName ?? string.Empty,
            Username = profile.Username,
            PhoneNumber = profile.PhoneNumber,
            Email = profile.Email
        });
    }

    [HttpGet]
    public async Task<IActionResult> Account(CancellationToken cancellationToken)
    {
        if (IsStaff())
            return RedirectToAction(nameof(Index));

        var profile = await GetCurrentProfileAsync(cancellationToken);
        if (profile is null)
            return RedirectToAction("Login", "Account");

        return View(new ProfileViewModel
        {
            BusinessName = profile.BusinessName ?? string.Empty,
            Username = profile.Username,
            FullName = profile.FullName,
            PhoneNumber = profile.PhoneNumber,
            Email = profile.Email,
            Address = profile.Address,
            ProfilePhotoUrl = profile.ProfilePhotoUrl
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [RequestSizeLimit(6 * 1024 * 1024)]
    public async Task<IActionResult> Account(ProfileViewModel model, CancellationToken cancellationToken)
    {
        if (IsStaff())
            return RedirectToAction(nameof(Index));

        var profile = await GetCurrentProfileAsync(cancellationToken);
        if (profile is null)
            return RedirectToAction("Login", "Account");

        var currentPhotoUrl = profile.ProfilePhotoUrl;
        // Email is the Supabase sign-in identity. Do not change it in the
        // profile row without completing Supabase's email verification flow.
        model.Email = profile.Email;
        ModelState.Remove(nameof(model.Email));
        model.ProfilePhotoUrl = currentPhotoUrl;

        if (!ModelState.IsValid)
            return View(model);

        var username = model.Username.Trim();
        var usernameTaken = await _dbContext.Profiles
            .AsNoTracking()
            .AnyAsync(p => p.Id != profile.Id && p.Username.ToLower() == username.ToLower(), cancellationToken);

        if (usernameTaken)
        {
            ModelState.AddModelError(nameof(model.Username), "This username is already taken.");
            return View(model);
        }

        string? uploadedPhotoUrl = null;
        if (model.ProfilePhoto is not null)
        {
            if (model.ProfilePhoto.Length == 0 || model.ProfilePhoto.Length > 5 * 1024 * 1024)
            {
                ModelState.AddModelError(nameof(model.ProfilePhoto), "Choose a profile photo up to 5 MB.");
                return View(model);
            }

            var extension = Path.GetExtension(model.ProfilePhoto.FileName).ToLowerInvariant();
            var allowedTypes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [".jpg"] = "image/jpeg",
                [".jpeg"] = "image/jpeg",
                [".png"] = "image/png",
                [".webp"] = "image/webp"
            };

            if (!allowedTypes.TryGetValue(extension, out var expectedContentType) ||
                !string.Equals(model.ProfilePhoto.ContentType, expectedContentType, StringComparison.OrdinalIgnoreCase))
            {
                ModelState.AddModelError(nameof(model.ProfilePhoto), "Only JPG, PNG or WebP profile photos are allowed.");
                return View(model);
            }

            uploadedPhotoUrl = await UploadProfilePhotoAsync(model.ProfilePhoto, profile.Id, cancellationToken);
            if (uploadedPhotoUrl is null)
            {
                ModelState.AddModelError(nameof(model.ProfilePhoto), "The profile photo could not be uploaded. Please try again.");
                return View(model);
            }
        }

        profile.BusinessName = model.BusinessName.Trim();
        profile.Username = username;
        profile.FullName = string.IsNullOrWhiteSpace(model.FullName) ? null : model.FullName.Trim();
        profile.PhoneNumber = string.IsNullOrWhiteSpace(model.PhoneNumber) ? null : model.PhoneNumber.Trim();
        profile.Address = string.IsNullOrWhiteSpace(model.Address) ? null : model.Address.Trim();
        profile.ProfilePhotoUrl = uploadedPhotoUrl ?? currentPhotoUrl;

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update account profile {ProfileId}.", profile.Id);
            ModelState.AddModelError(string.Empty, "Your profile could not be saved. Please try again.");
            model.Email = profile.Email;
            model.ProfilePhotoUrl = currentPhotoUrl;
            return View(model);
        }

        HttpContext.Session.SetString("BusinessName", profile.BusinessName ?? string.Empty);
        HttpContext.Session.SetString("Username", profile.Username);

        TempData["ProfileMessage"] = "Profile updated successfully.";
        return RedirectToAction(nameof(Account));
    }

    [HttpGet]
    public async Task<IActionResult> BusinessSummary(CancellationToken cancellationToken)
    {
        if (string.Equals(HttpContext.Session.GetString("UserRole"), "Staff", StringComparison.OrdinalIgnoreCase))
            return RedirectToAction(nameof(Index));

        if (!long.TryParse(HttpContext.Session.GetString("ProfileId"), out var profileId))
            return RedirectToAction("Login", "Account");

        var indiaTimeZone = TimeZoneInfo.FindSystemTimeZoneById(
            OperatingSystem.IsWindows() ? "India Standard Time" : "Asia/Kolkata");
        var todayIndia = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, indiaTimeZone));
        var monthStartUtc = TimeZoneInfo.ConvertTimeToUtc(
            new DateTime(todayIndia.Year, todayIndia.Month, 1, 0, 0, 0, DateTimeKind.Unspecified),
            indiaTimeZone);
        var tomorrowUtc = TimeZoneInfo.ConvertTimeToUtc(
            todayIndia.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified),
            indiaTimeZone);

        var monthBills = _dbContext.SalesBills
            .AsNoTracking()
            .Where(bill =>
                bill.ProfileId == profileId &&
                bill.Status == "Completed" &&
                bill.CreatedAt >= monthStartUtc &&
                bill.CreatedAt < tomorrowUtc);

        var monthSales = await monthBills
            .SumAsync(bill => bill.TotalAmount, cancellationToken);

        var monthBillIds = await monthBills
            .Select(bill => bill.BillId)
            .ToListAsync(cancellationToken);

        var monthItems = await _dbContext.SalesBillItems
            .AsNoTracking()
            .Where(item => monthBillIds.Contains(item.BillId))
            .Select(item => new
            {
                item.LineTotal,
                item.Quantity,
                item.PurchaseUnitPrice
            })
            .ToListAsync(cancellationToken);

        var todaySales = await _dbContext.SalesBills
            .AsNoTracking()
            .Where(bill =>
                bill.ProfileId == profileId &&
                bill.Status == "Completed" &&
                bill.CreatedAt >= tomorrowUtc.Subtract(TimeSpan.FromDays(1)) &&
                bill.CreatedAt < tomorrowUtc)
            .SumAsync(bill => bill.TotalAmount, cancellationToken);

        var stockValue = await _dbContext.ProductBatches
            .AsNoTracking()
            .Where(batch =>
                batch.Product.ProfileId == profileId &&
                batch.Product.IsActive &&
                batch.IsActive &&
                !batch.IsQuarantined &&
                batch.QuantityOnHand > 0)
            .SumAsync(batch => batch.QuantityOnHand * batch.SellingUnitPrice, cancellationToken);

        var customerBalances = await _dbContext.CustomerLedgerEntries
            .AsNoTracking()
            .Where(entry => entry.ProfileId == profileId)
            .GroupBy(entry => entry.CustomerId)
            .Select(group => group.Sum(entry => entry.BalanceChange))
            .ToListAsync(cancellationToken);

        return View(new BusinessSummaryViewModel
        {
            TodaySales = todaySales,
            MonthSales = monthSales,
            MonthProfit = monthItems.Sum(item => item.LineTotal - item.PurchaseUnitPrice * item.Quantity),
            StockValue = stockValue,
            CustomerOutstanding = customerBalances.Where(balance => balance > 0m).Sum(),
            CustomersWithOutstanding = customerBalances.Count(balance => balance > 0m)
        });
    }

    [HttpGet]
    public IActionResult UnlimitedPlan() => View();

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Index(ProfileViewModel model, CancellationToken cancellationToken)
    {
        if (string.Equals(HttpContext.Session.GetString("UserRole"), "Staff", StringComparison.OrdinalIgnoreCase))
            return RedirectToAction(nameof(Index));

        var profile = await GetCurrentProfileAsync(cancellationToken);
        if (profile is null)
            return RedirectToAction("Login", "Account");

        if (!ModelState.IsValid)
            return View(model);

        var username = model.Username.Trim();
        var usernameTaken = await _dbContext.Profiles
            .AsNoTracking()
            .AnyAsync(p => p.Id != profile.Id && p.Username.ToLower() == username.ToLower(), cancellationToken);

        if (usernameTaken)
        {
            ModelState.AddModelError(nameof(model.Username), "This username is already taken.");
            return View(model);
        }

        profile.BusinessName = model.BusinessName.Trim();
        profile.Username = username;
        profile.PhoneNumber = string.IsNullOrWhiteSpace(model.PhoneNumber)
            ? null
            : model.PhoneNumber.Trim();

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update profile {ProfileId}.", profile.Id);
            ModelState.AddModelError(
                string.Empty,
                "Your profile could not be saved. No changes were applied.");
            return View(model);
        }

        HttpContext.Session.SetString("BusinessName", profile.BusinessName);
        HttpContext.Session.SetString("Username", profile.Username);

        TempData["ProfileMessage"] = "Profile updated successfully.";
        return RedirectToAction(nameof(Index));
    }

    [HttpGet]
    public IActionResult Feedback() => View(new FeedbackViewModel());

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Feedback(FeedbackViewModel model, CancellationToken cancellationToken)
    {
        var allowedTags = new[]
        {
            "Usability",
            "App Design",
            "Performance",
            "Customer Support",
            "Features"
        };

        model.SelectedTags = model.SelectedTags
            .Where(tag => !string.IsNullOrWhiteSpace(tag))
            .Select(tag => tag.Trim())
            .Where(tag => allowedTags.Contains(tag, StringComparer.Ordinal))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        if (!ModelState.IsValid)
            return View(model);

        var profile = await GetCurrentProfileAsync(cancellationToken);
        if (profile is null)
            return RedirectToAction("Login", "Account");

        var feedback = new Feedback
        {
            ProfileId = profile.Id,
            Rating = model.Rating,
            Message = model.Message.Trim(),
            Tags = model.SelectedTags.Count == 0 ? null : string.Join(", ", model.SelectedTags),
            CreatedAt = DateTime.UtcNow
        };

        _dbContext.Set<Feedback>().Add(feedback);
        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save feedback for profile {ProfileId}.", profile.Id);
            ModelState.AddModelError(
                string.Empty,
                "Your feedback could not be submitted. Please try again.");
            return View(model);
        }

        TempData["FeedbackMessage"] = "Thank you. Your feedback has been submitted.";
        return RedirectToAction(nameof(Feedback));
    }

    private async Task<string?> UploadProfilePhotoAsync(
        IFormFile file,
        long profileId,
        CancellationToken cancellationToken)
    {
        var accessToken = HttpContext.Session.GetString("SupabaseAccessToken");
        var configuration = HttpContext.RequestServices.GetRequiredService<IConfiguration>();
        var baseUrl = configuration["Supabase:Url"]?.TrimEnd('/');
        var anonKey = configuration["Supabase:AnonKey"];

        if (string.IsNullOrWhiteSpace(accessToken) ||
            string.IsNullOrWhiteSpace(baseUrl) ||
            string.IsNullOrWhiteSpace(anonKey))
            return null;

        var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
        var objectPath = $"profile/{profileId}/{Guid.NewGuid():N}{extension}";
        var client = _httpClientFactory.CreateClient();

        using var stream = file.OpenReadStream();
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"{baseUrl}/storage/v1/object/staff-photos/{objectPath}");
        request.Headers.Add("apikey", anonKey);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        request.Headers.Add("x-upsert", "false");
        request.Content = new StreamContent(stream);
        request.Content.Headers.ContentType = new MediaTypeHeaderValue(file.ContentType);

        using var response = await client.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("Profile photo upload failed with status {StatusCode}.", response.StatusCode);
            return null;
        }

        return $"{baseUrl}/storage/v1/object/public/staff-photos/{objectPath}";
    }

    private bool IsStaff() =>
        string.Equals(
            HttpContext.Session.GetString("UserRole"),
            "Staff",
            StringComparison.OrdinalIgnoreCase);

    private async Task<Models.Profile?> GetCurrentProfileAsync(CancellationToken cancellationToken)
    {
        var profileId = HttpContext.Session.GetString("ProfileId");
        if (!long.TryParse(profileId, out var id))
            return null;

        return await _dbContext.Profiles
            .SingleOrDefaultAsync(p => p.Id == id, cancellationToken);
    }
}
