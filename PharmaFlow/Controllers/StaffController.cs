using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Net.Http.Headers;
using PharmaFlow.Data;
using PharmaFlow.Filters;
using PharmaFlow.Models;
using PharmaFlow.Models.ViewModels;
using PharmaFlow.Services;

namespace PharmaFlow.Controllers;

[SessionAuthorize]
public sealed class StaffController : Controller
{
    private readonly ApplicationDbContext _dbContext;
    private readonly ISupabaseAuthService _authService;
    private readonly ILogger<StaffController> _logger;
    private readonly IHttpClientFactory _httpClientFactory;

    public StaffController(
        ApplicationDbContext dbContext,
        ISupabaseAuthService authService,
        ILogger<StaffController> logger,
        IHttpClientFactory httpClientFactory)
    {
        _dbContext = dbContext;
        _authService = authService;
        _logger = logger;
        _httpClientFactory = httpClientFactory;
    }

    [HttpGet]
    public IActionResult Add()
    {
        if (!IsOwner())
            return RedirectToAction("Index", "Home");

        return View(new AddStaffViewModel());
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Add(AddStaffViewModel model, CancellationToken cancellationToken)
    {
        if (!IsOwner())
            return RedirectToAction("Index", "Home");

        if (!ModelState.IsValid)
            return View(model);

        if (!long.TryParse(HttpContext.Session.GetString("ProfileId"), out var profileId))
            return RedirectToAction("Login", "Account");

        var email = string.IsNullOrWhiteSpace(model.Email) ? null : model.Email.Trim().ToLowerInvariant();
        var phone = NormalizePhone(model.PhoneNumber);

        if (email is null && phone is null)
        {
            ModelState.AddModelError(string.Empty, "Enter a phone number or email address for staff login.");
            return View(model);
        }

        if (email is not null && await _dbContext.Staff.AnyAsync(s => s.ProfileId == profileId && s.Email == email, cancellationToken))
        {
            ModelState.AddModelError(nameof(model.Email), "This email is already assigned to a staff account.");
            return View(model);
        }

        if (phone is not null && await _dbContext.Staff.AnyAsync(s => s.ProfileId == profileId && s.PhoneNumber == phone, cancellationToken))
        {
            ModelState.AddModelError(nameof(model.PhoneNumber), "This phone number is already assigned to a staff account.");
            return View(model);
        }

        string? profilePhotoUrl = null;
        if (model.ProfilePhoto is not null)
        {
            if (model.ProfilePhoto.Length > 5 * 1024 * 1024)
            {
                ModelState.AddModelError(nameof(model.ProfilePhoto), "Profile photo must be 5 MB or smaller.");
                return View(model);
            }

            var allowedTypes = new[] { "image/jpeg", "image/png", "image/webp" };
            if (!allowedTypes.Contains(model.ProfilePhoto.ContentType, StringComparer.OrdinalIgnoreCase))
            {
                ModelState.AddModelError(nameof(model.ProfilePhoto), "Only JPG, PNG or WebP profile photos are allowed.");
                return View(model);
            }

            profilePhotoUrl = await UploadStaffPhotoAsync(model.ProfilePhoto, profileId, cancellationToken);
            if (profilePhotoUrl is null)
            {
                ModelState.AddModelError(nameof(model.ProfilePhoto), "The profile photo could not be uploaded. Please try again.");
                return View(model);
            }
        }

        var authResult = await _authService.SignUpStaffAsync(
            email,
            phone,
            model.Password,
            model.FullName.Trim(),
            cancellationToken);

        if (!authResult.Success || string.IsNullOrWhiteSpace(authResult.UserId) || !Guid.TryParse(authResult.UserId, out var authUserId))
        {
            ModelState.AddModelError(string.Empty, authResult.Error ?? "Unable to create the staff login.");
            return View(model);
        }

        var staff = new Staff
        {
            ProfileId = profileId,
            AuthUserId = authUserId,
            FullName = model.FullName.Trim(),
            Address = string.IsNullOrWhiteSpace(model.Address) ? null : model.Address.Trim(),
            PhoneNumber = phone,
            Email = email,
            ProfilePhotoUrl = profilePhotoUrl,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };

        try
        {
            _dbContext.Staff.Add(staff);
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save staff record after Supabase account creation.");
            ModelState.AddModelError(string.Empty, "The staff login was created, but the staff profile could not be saved. Please contact support before trying again.");
            return View(model);
        }

        TempData["StaffMessage"] = $"{staff.FullName} was added successfully.";
        return RedirectToAction("Index", "Profile");
    }

    private async Task<string?> UploadStaffPhotoAsync(IFormFile file, long profileId, CancellationToken cancellationToken)
    {
        var accessToken = HttpContext.Session.GetString("SupabaseAccessToken");
        var baseUrl = HttpContext.RequestServices.GetRequiredService<IConfiguration>()["Supabase:Url"]?.TrimEnd('/');
        var anonKey = HttpContext.RequestServices.GetRequiredService<IConfiguration>()["Supabase:AnonKey"];

        if (string.IsNullOrWhiteSpace(accessToken) || string.IsNullOrWhiteSpace(baseUrl) || string.IsNullOrWhiteSpace(anonKey))
            return null;

        var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (extension is not ".jpg" and not ".jpeg" and not ".png" and not ".webp")
            extension = ".jpg";

        var objectPath = $"{profileId}/{Guid.NewGuid():N}{extension}";
        var client = _httpClientFactory.CreateClient();
        using var stream = await file.OpenReadStreamAsync(cancellationToken);
        using var content = new StreamContent(stream);
        content.Headers.ContentType = new MediaTypeHeaderValue(file.ContentType);

        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"{baseUrl}/storage/v1/object/staff-photos/{Uri.EscapeDataString(objectPath)}");
        request.Headers.Add("apikey", anonKey);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        request.Headers.Add("x-upsert", "false");
        request.Content = content;

        using var response = await client.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("Staff photo upload failed with status {StatusCode}.", response.StatusCode);
            return null;
        }

        return $"{baseUrl}/storage/v1/object/public/staff-photos/{objectPath}";
    }

    private bool IsOwner() =>
        !string.Equals(
            HttpContext.Session.GetString("UserRole"),
            "Staff",
            StringComparison.OrdinalIgnoreCase);

    private static string? NormalizePhone(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var digits = new string(value.Where(char.IsDigit).ToArray());

        if (digits.StartsWith("91") && digits.Length == 12)
            return "+" + digits;

        if (digits.Length == 10)
            return "+91" + digits;

        return value.Trim();
    }
}
