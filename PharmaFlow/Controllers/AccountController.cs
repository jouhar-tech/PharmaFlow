using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PharmaFlow.Data;
using PharmaFlow.Models;
using PharmaFlow.Models.ViewModels;
using PharmaFlow.Services;

namespace PharmaFlow.Controllers;

public class AccountController : Controller
{
    private readonly ISupabaseAuthService _authService;
    private readonly ApplicationDbContext _dbContext;

    public AccountController(ISupabaseAuthService authService, ApplicationDbContext dbContext)
    {
        _authService = authService;
        _dbContext = dbContext;
    }

    [HttpGet]
    public IActionResult Login() => View(new LoginViewModel());

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Login(LoginViewModel model, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid) return View(model);

        var username = model.Username.Trim();
        var profile = await _dbContext.Profiles
            .AsNoTracking()
            .SingleOrDefaultAsync(p => p.Username == username, cancellationToken);

        if (profile is null || string.IsNullOrWhiteSpace(profile.Email))
        {
            ModelState.AddModelError(string.Empty, "Invalid username or password.");
            return View(model);
        }

        var result = await _authService.LoginAsync(profile.Email, model.Password, cancellationToken);
        if (!result.Success || string.IsNullOrWhiteSpace(result.UserId) || !Guid.TryParse(result.UserId, out _))
        {
            ModelState.AddModelError(string.Empty, "Invalid username or password.");
            return View(model);
        }

        SetAuthenticatedSession(result, profile.Id, profile.BusinessName);
        return RedirectAfterAuthentication(profile.BusinessName);
    }

    [HttpGet]
    public IActionResult Signup() => View(new SignupViewModel());

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Signup(SignupViewModel model, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid) return View(model);

        var username = model.Username.Trim();
        var usernameExists = await _dbContext.Profiles
            .AsNoTracking()
            .AnyAsync(p => p.Username == username, cancellationToken);

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

        SetAuthenticatedSession(result, profile.Id, profile.BusinessName);
        return RedirectAfterAuthentication(profile.BusinessName);
    }

    private void SetAuthenticatedSession(SupabaseAuthResult result, long profileId, string? businessName)
    {
        HttpContext.Session.SetString("SupabaseAccessToken", result.AccessToken!);
        HttpContext.Session.SetString("SupabaseUserId", result.UserId!);
        HttpContext.Session.SetString("ProfileId", profileId.ToString());

        if (!string.IsNullOrWhiteSpace(businessName))
            HttpContext.Session.SetString("BusinessName", businessName);
        else
            HttpContext.Session.Remove("BusinessName");
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
        while (await _dbContext.Profiles.AnyAsync(p => p.Username == username, cancellationToken))
            username = $"{baseUsername}{suffix++}";

        return username;
    }
}
