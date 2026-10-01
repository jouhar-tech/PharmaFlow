using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PharmaFlow.Data;
using PharmaFlow.Filters;
using PharmaFlow.Models;
using PharmaFlow.Services;

namespace PharmaFlow.Controllers;

[SessionAuthorize]
public sealed class NotificationsController : Controller
{
    private readonly ApplicationDbContext _db;
    private readonly IPharmaFlowPushNotificationService _notifications;

    public NotificationsController(
        ApplicationDbContext db,
        IPharmaFlowPushNotificationService notifications)
    {
        _db = db;
        _notifications = notifications;
    }

    [HttpGet]
    public IActionResult PublicKey()
    {
        var publicKey = HttpContext.RequestServices
            .GetRequiredService<IConfiguration>()["WebPush:PublicKey"];

        return string.IsNullOrWhiteSpace(publicKey)
            ? StatusCode(StatusCodes.Status503ServiceUnavailable)
            : Json(new { publicKey });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Subscribe(
        [FromBody] PushSubscriptionRequest request,
        CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        if (!Uri.TryCreate(request.Endpoint, UriKind.Absolute, out var endpoint) ||
            endpoint.Scheme != Uri.UriSchemeHttps)
            return BadRequest(new { message = "Invalid push endpoint." });

        if (request.P256dh.Length is < 20 or > 500 ||
            request.Auth.Length is < 10 or > 500)
            return BadRequest(new { message = "Invalid push subscription keys." });

        if (!long.TryParse(HttpContext.Session.GetString("ProfileId"), out var profileId))
            return Unauthorized();

        var profileActive = await _db.Profiles
            .AsNoTracking()
            .AnyAsync(p => p.Id == profileId && p.ActiveStatus == 1, cancellationToken);

        if (!profileActive)
            return Unauthorized();

        var role = HttpContext.Session.GetString("UserRole") ?? "Owner";
        var now = DateTime.UtcNow;

        var existing = await _db.PushDeviceSubscriptions
            .SingleOrDefaultAsync(s => s.Endpoint == request.Endpoint, cancellationToken);

        if (existing is null)
        {
            _db.PushDeviceSubscriptions.Add(new PushDeviceSubscription
            {
                ProfileId = profileId,
                UserRole = role,
                Endpoint = request.Endpoint,
                P256dh = request.P256dh,
                Auth = request.Auth,
                UserAgent = Request.Headers.UserAgent.ToString(),
                IsActive = true,
                CreatedAt = now,
                UpdatedAt = now
            });
        }
        else
        {
            existing.ProfileId = profileId;
            existing.UserRole = role;
            existing.P256dh = request.P256dh;
            existing.Auth = request.Auth;
            existing.UserAgent = Request.Headers.UserAgent.ToString();
            existing.IsActive = true;
            existing.UpdatedAt = now;
        }

        await _db.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Unsubscribe(
        [FromBody] UnsubscribeRequest request,
        CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        if (!long.TryParse(HttpContext.Session.GetString("ProfileId"), out var profileId))
            return Unauthorized();

        await _db.PushDeviceSubscriptions
            .Where(s => s.ProfileId == profileId && s.Endpoint == request.Endpoint)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(s => s.IsActive, false)
                .SetProperty(s => s.UpdatedAt, DateTime.UtcNow),
                cancellationToken);

        return NoContent();
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Test(CancellationToken cancellationToken)
    {
        if (!long.TryParse(HttpContext.Session.GetString("ProfileId"), out var profileId))
            return Unauthorized();

        var sent = await _notifications.SendTestNotificationAsync(profileId, cancellationToken);
        return sent ? Ok() : StatusCode(StatusCodes.Status503ServiceUnavailable);
    }

    public sealed class PushSubscriptionRequest
    {
        [Required, StringLength(2000)]
        public string Endpoint { get; set; } = string.Empty;

        [Required, StringLength(500)]
        public string P256dh { get; set; } = string.Empty;

        [Required, StringLength(500)]
        public string Auth { get; set; } = string.Empty;
    }

    public sealed class UnsubscribeRequest
    {
        [Required, StringLength(2000)]
        public string Endpoint { get; set; } = string.Empty;
    }
}
