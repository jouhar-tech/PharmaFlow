using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
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

    public StaffController(
        ApplicationDbContext dbContext,
        ISupabaseAuthService authService,
        ILogger<StaffController> logger)
    {
        _dbContext = dbContext;
        _authService = authService;
        _logger = logger;
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
            ProfilePhotoUrl = string.IsNullOrWhiteSpace(model.ProfilePhotoUrl) ? null : model.ProfilePhotoUrl.Trim(),
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
