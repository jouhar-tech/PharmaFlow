using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PharmaFlow.Data;
using PharmaFlow.Filters;
using PharmaFlow.Models.ViewModels;

namespace PharmaFlow.Controllers;

[SessionAuthorize]
public sealed class BusinessController : Controller
{
    private readonly ApplicationDbContext _dbContext;
    private readonly ILogger<BusinessController> _logger;

    public BusinessController(
        ApplicationDbContext dbContext,
        ILogger<BusinessController> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    [HttpGet]
    public async Task<IActionResult> Setup(CancellationToken cancellationToken)
    {
        var profileId = HttpContext.Session.GetString("ProfileId");
        if (!long.TryParse(profileId, out var id))
            return RedirectToAction("Login", "Account");

        var profile = await _dbContext.Profiles.FindAsync(new object[] { id }, cancellationToken);
        if (profile is null)
            return RedirectToAction("Login", "Account");

        if (!string.IsNullOrWhiteSpace(profile.BusinessName))
            return RedirectToAction("Index", "Home");

        return View(new BusinessSetupViewModel
        {
            PhoneNumber = profile.PhoneNumber
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Setup(BusinessSetupViewModel model, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
            return View(model);

        var profileId = HttpContext.Session.GetString("ProfileId");
        if (!long.TryParse(profileId, out var id))
            return RedirectToAction("Login", "Account");

        var profile = await _dbContext.Profiles.FindAsync(new object[] { id }, cancellationToken);
        if (profile is null)
            return RedirectToAction("Login", "Account");

        var googlePhoneRequired = HttpContext.Session.GetString("GoogleProfileSetupRequired") == "true";

        if (googlePhoneRequired && string.IsNullOrWhiteSpace(model.PhoneNumber))
        {
            ModelState.AddModelError(nameof(model.PhoneNumber), "Mobile number is required for Google sign-in.");
            return View(model);
        }

        profile.BusinessName = model.BusinessName.Trim();

        if (googlePhoneRequired)
            profile.PhoneNumber = model.PhoneNumber!.Trim();

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save business setup for profile {ProfileId}.", profile.Id);
            ModelState.AddModelError(
                string.Empty,
                "Your business details could not be saved. Please try again.");
            return View(model);
        }

        HttpContext.Session.SetString("BusinessName", profile.BusinessName);
        HttpContext.Session.Remove("GoogleProfileSetupRequired");
        return RedirectToAction("Index", "Home");
    }
}
