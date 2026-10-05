using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PharmaFlow.Data;
using PharmaFlow.Filters;

namespace PharmaFlow.Controllers;

[SessionAuthorize]
public sealed class SupportController : Controller
{
    private const string WhatsAppNumber = "919611678864";
    private const string SupportEmail = "mahammadjouhar@gmail.com";

    private readonly ApplicationDbContext _dbContext;

    public SupportController(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    [HttpGet]
    public async Task<IActionResult> WhatsApp(CancellationToken cancellationToken)
    {
        var profile = await GetCurrentProfileAsync(cancellationToken);
        if (profile is null)
            return RedirectToAction("Login", "Account");

        var message = BuildSupportMessage(profile);
        var url = $"https://wa.me/{WhatsAppNumber}?text={Uri.EscapeDataString(message)}";

        return Redirect(url);
    }

    [HttpGet]
    public async Task<IActionResult> Email(CancellationToken cancellationToken)
    {
        var profile = await GetCurrentProfileAsync(cancellationToken);
        if (profile is null)
            return RedirectToAction("Login", "Account");

        var ticketLabel = $"PF-{profile.Id:D6}";
        var subject = $"PharmaFlow Customer Support - {ticketLabel}";
        var body = BuildSupportMessage(profile);

        var url = $"mailto:{SupportEmail}?subject={Uri.EscapeDataString(subject)}&body={Uri.EscapeDataString(body)}";
        return Redirect(url);
    }

    private async Task<Models.Profile?> GetCurrentProfileAsync(CancellationToken cancellationToken)
    {
        var profileId = HttpContext.Session.GetString("ProfileId");
        if (!long.TryParse(profileId, out var id))
            return null;

        return await _dbContext.Profiles
            .AsNoTracking()
            .SingleOrDefaultAsync(p => p.Id == id, cancellationToken);
    }

    private static string BuildSupportMessage(Models.Profile profile)
    {
        var name = string.IsNullOrWhiteSpace(profile.FullName)
            ? profile.Username
            : profile.FullName.Trim();

        var shopName = string.IsNullOrWhiteSpace(profile.BusinessName)
            ? "Not provided"
            : profile.BusinessName.Trim();

        var plan = string.Equals(profile.SubscriptionPlan, "unlimited", StringComparison.OrdinalIgnoreCase)
            && (!profile.SubscriptionEndsAt.HasValue || profile.SubscriptionEndsAt.Value > DateTime.UtcNow)
            ? "Unlimited User"
            : "Free User";

        var lines = new List<string>
        {
            "Hi PharmaFlow Customer Support,",
            string.Empty,
            "I need assistance with PharmaFlow.",
            string.Empty,
            $"Profile ID: {profile.Id}",
            $"Name: {name}",
            $"Shop Name: {shopName}"
        };

        if (!string.IsNullOrWhiteSpace(profile.Address))
            lines.Add($"Address: {profile.Address.Trim()}");

        lines.Add($"Plan: {plan}");
        lines.Add(string.Empty);
        lines.Add("Issue / Request:");

        return string.Join(Environment.NewLine, lines);
    }
}
