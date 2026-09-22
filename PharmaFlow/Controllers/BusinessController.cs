using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PharmaFlow.Data;
using PharmaFlow.Models.ViewModels;

namespace PharmaFlow.Controllers;

public sealed class BusinessController : Controller
{
    private readonly ApplicationDbContext _dbContext;

    public BusinessController(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
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

        return View(new BusinessSetupViewModel());
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

        profile.BusinessName = model.BusinessName.Trim();
        await _dbContext.SaveChangesAsync(cancellationToken);

        HttpContext.Session.SetString("BusinessName", profile.BusinessName);
        return RedirectToAction("Index", "Home");
    }
}
