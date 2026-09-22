using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PharmaFlow.Data;
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
        if (!result.Success || string.IsNullOrWhiteSpace(result.UserId) || !Guid.TryParse(result.UserId, out var userId))
        {
            ModelState.AddModelError(string.Empty, "Invalid username or password.");
            return View(model);
        }

        HttpContext.Session.SetString("SupabaseAccessToken", result.AccessToken!);
        HttpContext.Session.SetString("SupabaseUserId", result.UserId);
        HttpContext.Session.SetString("ProfileId", profile.Id.ToString());
        return RedirectToAction("Index", "Home");
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
            model.Email.Trim(),
            model.Password,
            username,
            model.PhoneNumber,
            cancellationToken);

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
        return Redirect(_authService.GetGoogleLoginUrl(redirectUri));
    }

    [HttpGet]
    public IActionResult GoogleCallback(string? code, string? error)
    {
        TempData["AuthError"] = string.IsNullOrWhiteSpace(error) && !string.IsNullOrWhiteSpace(code)
            ? "Google authorization received. Token exchange configuration is still required."
            : "Google sign-in could not be completed.";
        return RedirectToAction(nameof(Login));
    }
}
