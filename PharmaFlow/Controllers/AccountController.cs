using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PharmaFlow.Data;
using PharmaFlow.Models;
using PharmaFlow.Models.ViewModels;
using PharmaFlow.Services;

namespace PharmaFlow.Controllers;

public class AccountController : Controller
{
    private readonly ISupabaseAuthService _authService;
    private readonly ApplicationDbContext _dbContext;
    private readonly ILogger<AccountController> _logger;

    public AccountController(
        ISupabaseAuthService authService,
        ApplicationDbContext dbContext,
        ILogger<AccountController> logger)
    {
        _authService = authService;
        _dbContext = dbContext;
        _logger = logger;
    }

    [HttpGet]
    public IActionResult Login() => View(new LoginViewModel());

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Login(LoginViewModel model, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid) return View(model);

        var identifier = model.Username.Trim();
        var normalizedPhone = NormalizePhone(identifier);

        var staff = await _dbContext.Staff
            .AsNoTracking()
            .SingleOrDefaultAsync(s =>
                s.IsActive &&
                ((normalizedPhone != null && s.PhoneNumber == normalizedPhone) ||
                 (identifier.Contains("@") && s.Email == identifier.ToLowerInvariant())),
                cancellationToken);

        if (staff is not null)
        {
            SupabaseAuthResult staffResult;

            if (!string.IsNullOrWhiteSpace(staff.Email) &&
                string.Equals(staff.Email, identifier, StringComparison.OrdinalIgnoreCase))
                staffResult = await _authService.LoginAsync(staff.Email, model.Password, cancellationToken);
            else if (!string.IsNullOrWhiteSpace(staff.PhoneNumber) && normalizedPhone == staff.PhoneNumber)
                staffResult = await _authService.LoginWithPhoneAsync(staff.PhoneNumber, model.Password, cancellationToken);
            else
            {
                ModelState.AddModelError(string.Empty, "Invalid login details.");
                return View(model);
            }

            if (!staffResult.Success ||
                string.IsNullOrWhiteSpace(staffResult.UserId) ||
                !Guid.TryParse(staffResult.UserId, out var staffUserId) ||
                staffUserId != staff.AuthUserId)
            {
                ModelState.AddModelError(string.Empty, "Invalid login details.");
                return View(model);
            }

            var profile = await _dbContext.Profiles
                .AsNoTracking()
                .SingleOrDefaultAsync(p => p.Id == staff.ProfileId, cancellationToken);

            if (profile is null)
            {
                ModelState.AddModelError(string.Empty, "This pharmacy account is no longer available.");
                return View(model);
            }

            SetStaffAuthenticatedSession(staffResult, staff, profile);
            await MarkStaffActiveAsync(staff.StaffId, cancellationToken);
            return RedirectToAction("Index", "Home");
        }

        var normalizedUsername = identifier.ToLowerInvariant();

        var ownerProfile = await _dbContext.Profiles
            .AsNoTracking()
            .SingleOrDefaultAsync(p =>
                p.Username.ToLower() == normalizedUsername ||
                (!string.IsNullOrWhiteSpace(p.Email) && p.Email == identifier.ToLowerInvariant()) ||
                (!string.IsNullOrWhiteSpace(p.PhoneNumber) && normalizedPhone != null && p.PhoneNumber == normalizedPhone),
                cancellationToken);

        if (ownerProfile is null || string.IsNullOrWhiteSpace(ownerProfile.Email))
        {
            ModelState.AddModelError(string.Empty, "Invalid username, email, phone or password.");
            return View(model);
        }

        var result = await _authService.LoginAsync(ownerProfile.Email, model.Password, cancellationToken);
        if (!result.Success ||
            string.IsNullOrWhiteSpace(result.UserId) ||
            !Guid.TryParse(result.UserId, out var authenticatedUserId) ||
            authenticatedUserId != ownerProfile.UserId)
        {
            ModelState.AddModelError(string.Empty, "Invalid username, email, phone or password.");
            return View(model);
        }

        SetAuthenticatedSession(result, ownerProfile.Id, ownerProfile.BusinessName, ownerProfile.Username);
        await MarkProfileActiveAsync(ownerProfile.Id, cancellationToken);
        return RedirectAfterAuthentication(ownerProfile.BusinessName);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> TouchSession(CancellationToken cancellationToken)
    {
        var role = HttpContext.Session.GetString("UserRole");
        var profileIdText = HttpContext.Session.GetString("ProfileId");
        var accessToken = HttpContext.Session.GetString("SupabaseAccessToken");
        var userIdText = HttpContext.Session.GetString("SupabaseUserId");

        if (string.IsNullOrWhiteSpace(accessToken) ||
            string.IsNullOrWhiteSpace(userIdText) ||
            !Guid.TryParse(userIdText, out _) ||
            !long.TryParse(profileIdText, out var profileId))
        {
            return Unauthorized();
        }

        // Keep this endpoint cheap: one indexed profile lookup/update only when
        // the client decides a new app-open/resume event needs to be recorded.
        var profile = await _dbContext.Profiles
            .AsNoTracking()
            .Where(p => p.Id == profileId)
            .Select(p => new { p.Id, p.ActiveStatus, p.LastLoginAt })
            .SingleOrDefaultAsync(cancellationToken);

        if (profile is null || profile.ActiveStatus != 1)
        {
            HttpContext.Session.Clear();
            return Unauthorized();
        }

        var now = DateTime.UtcNow;
        var cutoff = now.AddDays(-60);

        if (profile.LastLoginAt.HasValue && profile.LastLoginAt.Value <= cutoff)
        {
            await _dbContext.Profiles
                .Where(p => p.Id == profileId && p.ActiveStatus == 1)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(p => p.ActiveStatus, (short)0)
                    .SetProperty(p => p.LastLogoutAt, now), cancellationToken);

            HttpContext.Session.Clear();
            return Unauthorized();
        }

        if (string.Equals(role, "Owner", StringComparison.OrdinalIgnoreCase))
        {
            await _dbContext.Profiles
                .Where(p => p.Id == profileId && p.ActiveStatus == 1)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(p => p.LastLoginAt, now), cancellationToken);
        }

        return NoContent();
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Logout(CancellationToken cancellationToken)
    {
        try
        {
            if (long.TryParse(HttpContext.Session.GetString("StaffId"), out var staffId))
                await MarkStaffInactiveAsync(staffId, cancellationToken);
            else if (long.TryParse(HttpContext.Session.GetString("ProfileId"), out var profileId))
                await MarkProfileInactiveAsync(profileId, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not update profile active status during logout.");
        }
        finally
        {
            HttpContext.Session.Clear();
        }

        TempData["AuthMessage"] = "You have been logged out successfully.";
        return RedirectToAction(nameof(Login));
    }

    [HttpGet]
    public IActionResult Signup() => View(new SignupViewModel());

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Signup(SignupViewModel model, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid) return View(model);

        var username = model.Username.Trim();
        var normalizedUsername = username.ToLowerInvariant();
        var usernameExists = await _dbContext.Profiles
            .AsNoTracking()
            .AnyAsync(
                p => p.Username.ToLower() == normalizedUsername,
                cancellationToken);

        if (usernameExists)
        {
            ModelState.AddModelError(nameof(model.Username), "This username is already taken.");
            return View(model);
        }

        var result = await _authService.SignUpAsync(
            model.Email.Trim(), model.Password, username, model.PhoneNumber, cancellationToken);

        if (!result.Success)
        {
            ModelState.AddModelError(string.Empty, result.Error ?? "Unable to create account.");
            return View(model);
        }

        if (!string.IsNullOrWhiteSpace(result.AccessToken))
            HttpContext.Session.SetString("SupabaseAccessToken", result.AccessToken);

        TempData["AuthMessage"] = "Account created successfully. Please check your email if verification is required.";
        return RedirectToAction(nameof(Login));
    }

    [HttpGet]
    public IActionResult GoogleLogin()
    {
        var redirectUri = Url.Action(nameof(GoogleCallback), "Account", null, Request.Scheme)!;
        var codeVerifier = SupabaseAuthService.CreateCodeVerifier();
        var codeChallenge = SupabaseAuthService.CreateCodeChallenge(codeVerifier);

        HttpContext.Session.SetString("GoogleCodeVerifier", codeVerifier);
        return Redirect(_authService.GetGoogleLoginUrl(redirectUri, codeChallenge));
    }

    [HttpGet]
    public async Task<IActionResult> GoogleCallback(string? code, string? error, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(error) || string.IsNullOrWhiteSpace(code))
        {
            TempData["AuthError"] = "Google sign-in could not be completed.";
            return RedirectToAction(nameof(Login));
        }

        var codeVerifier = HttpContext.Session.GetString("GoogleCodeVerifier");
        HttpContext.Session.Remove("GoogleCodeVerifier");

        if (string.IsNullOrWhiteSpace(codeVerifier))
        {
            TempData["AuthError"] = "Google sign-in session expired. Please try again.";
            return RedirectToAction(nameof(Login));
        }

        var result = await _authService.ExchangeGoogleCodeAsync(code, codeVerifier, cancellationToken);
        if (!result.Success || string.IsNullOrWhiteSpace(result.UserId) || !Guid.TryParse(result.UserId, out var userId))
        {
            TempData["AuthError"] = result.Error ?? "Google sign-in failed. Please try again.";
            return RedirectToAction(nameof(Login));
        }

        var profile = await _dbContext.Profiles.SingleOrDefaultAsync(p => p.UserId == userId, cancellationToken);
        var isNewGoogleProfile = profile is null;

        if (profile is null)
        {
            if (string.IsNullOrWhiteSpace(result.Email))
            {
                TempData["AuthError"] = "Google account email could not be retrieved.";
                return RedirectToAction(nameof(Login));
            }

            profile = new Profile
            {
                UserId = userId,
                Username = await CreateUniqueUsernameAsync(result.Email, cancellationToken),
                Email = result.Email,
                CreatedAt = DateTime.UtcNow
            };
            _dbContext.Profiles.Add(profile);
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        else if (string.IsNullOrWhiteSpace(profile.Email) && !string.IsNullOrWhiteSpace(result.Email))
        {
            profile.Email = result.Email;
            await _dbContext.SaveChangesAsync(cancellationToken);
        }

        SetAuthenticatedSession(result, profile.Id, profile.BusinessName, profile.Username);
        await MarkProfileActiveAsync(profile.Id, cancellationToken);

        if (isNewGoogleProfile)
            HttpContext.Session.SetString("GoogleProfileSetupRequired", "true");

        return RedirectAfterAuthentication(profile.BusinessName);
    }

    private void SetStaffAuthenticatedSession(SupabaseAuthResult result, Staff staff, Profile profile)
    {
        HttpContext.Session.SetString("SupabaseAccessToken", result.AccessToken!);
        HttpContext.Session.SetString("SupabaseUserId", result.UserId!);
        HttpContext.Session.SetString("ProfileId", profile.Id.ToString());
        HttpContext.Session.SetString("StaffId", staff.StaffId.ToString());
        HttpContext.Session.SetString("UserRole", "Staff");
        HttpContext.Session.SetString("Username", staff.FullName);

        if (!string.IsNullOrWhiteSpace(profile.BusinessName))
            HttpContext.Session.SetString("BusinessName", profile.BusinessName);
        else
            HttpContext.Session.Remove("BusinessName");
    }

    private async Task MarkStaffActiveAsync(long staffId, CancellationToken cancellationToken)
    {
        await _dbContext.Staff
            .Where(s => s.StaffId == staffId)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(s => s.LastLoginAt, DateTime.UtcNow), cancellationToken);
    }

    private async Task MarkStaffInactiveAsync(long staffId, CancellationToken cancellationToken)
    {
        await _dbContext.Staff
            .Where(s => s.StaffId == staffId)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(s => s.LastLogoutAt, DateTime.UtcNow), cancellationToken);
    }

    private static string? NormalizePhone(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var digits = new string(value.Where(char.IsDigit).ToArray());
        if (digits.StartsWith("91") && digits.Length == 12) return "+" + digits;
        if (digits.Length == 10) return "+91" + digits;
        return value.Trim();
    }

    private void SetAuthenticatedSession(SupabaseAuthResult result, long profileId, string? businessName, string username)
    {
        HttpContext.Session.SetString("SupabaseAccessToken", result.AccessToken!);
        HttpContext.Session.SetString("SupabaseUserId", result.UserId!);
        HttpContext.Session.SetString("ProfileId", profileId.ToString());
        HttpContext.Session.SetString("Username", username);
        HttpContext.Session.SetString("UserRole", "Owner");
        HttpContext.Session.Remove("StaffId");

        if (!string.IsNullOrWhiteSpace(businessName))
            HttpContext.Session.SetString("BusinessName", businessName);
        else
            HttpContext.Session.Remove("BusinessName");
    }

    private async Task MarkProfileActiveAsync(long profileId, CancellationToken cancellationToken)
    {
        try
        {
            var now = DateTime.UtcNow;

            // Keep LastLoginAt as the most recent real app activity.
            // NotificationCycleStartAt is initialized only once so the 30-day
            // savings cycle does not move forward every time the user opens the app.
            var existingCycleStart = await _dbContext.Profiles
                .AsNoTracking()
                .Where(p => p.Id == profileId)
                .Select(p => p.NotificationCycleStartAt)
                .SingleOrDefaultAsync(cancellationToken);

            var cycleStart = existingCycleStart ?? now;

            await _dbContext.Profiles
                .Where(p => p.Id == profileId)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(p => p.ActiveStatus, (short)1)
                    .SetProperty(p => p.LastLoginAt, now)
                    .SetProperty(p => p.NotificationCycleStartAt, cycleStart), cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not update profile activity during login for profile {ProfileId}.", profileId);
        }
    }

    private async Task MarkProfileInactiveAsync(long profileId, CancellationToken cancellationToken)
    {
        await _dbContext.Profiles
            .Where(p => p.Id == profileId)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(p => p.ActiveStatus, (short)0)
                .SetProperty(p => p.LastLogoutAt, DateTime.UtcNow), cancellationToken);
    }

    private IActionResult RedirectAfterAuthentication(string? businessName) =>
        string.IsNullOrWhiteSpace(businessName)
            ? RedirectToAction("Setup", "Business")
            : RedirectToAction("Index", "Home");

    private async Task<string> CreateUniqueUsernameAsync(string email, CancellationToken cancellationToken)
    {
        var localPart = email.Split('@')[0];
        var baseUsername = new string(localPart.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(baseUsername)) baseUsername = "user";

        var username = baseUsername;
        var suffix = 1;
        while (await _dbContext.Profiles.AnyAsync(
                   p => p.Username.ToLower() == username.ToLower(),
                   cancellationToken))
            username = $"{baseUsername}{suffix++}";

        return username;
    }
}
