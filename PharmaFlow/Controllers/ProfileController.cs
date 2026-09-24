using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PharmaFlow.Data;
using PharmaFlow.Filters;
using PharmaFlow.Models;
using PharmaFlow.Models.ViewModels;

namespace PharmaFlow.Controllers;

[SessionAuthorize]
public sealed class ProfileController : Controller
{
    private readonly ApplicationDbContext _dbContext;

    public ProfileController(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
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

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Index(ProfileViewModel model, CancellationToken cancellationToken)
    {
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

        await _dbContext.SaveChangesAsync(cancellationToken);

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
        if (!ModelState.IsValid)
            return View(model);

        var profile = await GetCurrentProfileAsync(cancellationToken);
        if (profile is null)
            return RedirectToAction("Login", "Account");

        var feedback = new Feedback
        {
            ProfileId = profile.Id,
            Message = model.Message.Trim(),
            CreatedAt = DateTime.UtcNow
        };

        _dbContext.Set<Feedback>().Add(feedback);
        await _dbContext.SaveChangesAsync(cancellationToken);

        TempData["FeedbackMessage"] = "Thank you. Your feedback has been submitted.";
        return RedirectToAction(nameof(Feedback));
    }

    private async Task<Models.Profile?> GetCurrentProfileAsync(CancellationToken cancellationToken)
    {
        var profileId = HttpContext.Session.GetString("ProfileId");
        if (!long.TryParse(profileId, out var id))
            return null;

        return await _dbContext.Profiles
            .SingleOrDefaultAsync(p => p.Id == id, cancellationToken);
    }
}
