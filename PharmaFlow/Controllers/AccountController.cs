using Microsoft.AspNetCore.Mvc;
using PharmaFlow.Models.ViewModels;
using PharmaFlow.Services;

namespace PharmaFlow.Controllers;

public class AccountController : Controller
{
    private readonly ISupabaseAuthService _authService;

    public AccountController(ISupabaseAuthService authService) => _authService = authService;

    [HttpGet]
    public IActionResult Login() => View(new LoginViewModel());

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Login(LoginViewModel model, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid) return View(model);
        var sent = await _authService.SendPhoneOtpAsync(model.PhoneNumber, cancellationToken);
        if (!sent)
        {
            ModelState.AddModelError(string.Empty, "Unable to send OTP. Please try again later.");
            return View(model);
        }
        TempData["OtpPhoneNumber"] = model.PhoneNumber;
        return RedirectToAction(nameof(VerifyOtp));
    }

    [HttpGet]
    public IActionResult VerifyOtp()
    {
        var phone = TempData.Peek("OtpPhoneNumber") as string;
        if (string.IsNullOrWhiteSpace(phone)) return RedirectToAction(nameof(Login));
        return View(new OtpViewModel { PhoneNumber = phone });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> VerifyOtp(OtpViewModel model, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid) return View(model);
        var result = await _authService.VerifyPhoneOtpAsync(model.PhoneNumber, model.Otp, cancellationToken);
        if (!result.Success)
        {
            ModelState.AddModelError(string.Empty, "Invalid or expired OTP.");
            return View(model);
        }
        HttpContext.Session.SetString("SupabaseAccessToken", result.AccessToken!);
        return RedirectToAction("Index", "Home");
    }

    [HttpGet]
    public IActionResult Signup() => View(new SignupViewModel());

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Signup(SignupViewModel model, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid) return View(model);
        var sent = await _authService.SendPhoneOtpAsync(model.PhoneNumber, cancellationToken);
        if (!sent)
        {
            ModelState.AddModelError(string.Empty, "Unable to start signup. Please try again later.");
            return View(model);
        }
        TempData["OtpPhoneNumber"] = model.PhoneNumber;
        TempData["SignupFullName"] = model.FullName.Trim();
        return RedirectToAction(nameof(VerifyOtp));
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
