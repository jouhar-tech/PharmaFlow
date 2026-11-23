using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.DependencyInjection;
using PharmaFlow.Services;

namespace PharmaFlow.Filters;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class SessionAuthorizeAttribute : Attribute, IAsyncActionFilter
{
    public async Task OnActionExecutionAsync(
        ActionExecutingContext context,
        ActionExecutionDelegate next)
    {
        var httpContext = context.HttpContext;
        var session = httpContext.Session;
        var userId = session.GetString("SupabaseUserId");
        var profileId = session.GetString("ProfileId");

        if (string.IsNullOrWhiteSpace(userId) ||
            !Guid.TryParse(userId, out _) ||
            !long.TryParse(profileId, out _))
        {
            context.Result = new RedirectToActionResult("Login", "Account", null);
            return;
        }

        var tokenProvider = httpContext.RequestServices
            .GetRequiredService<ISupabaseSessionTokenProvider>();

        var validAccessToken = await tokenProvider.GetValidAccessTokenAsync(
            httpContext,
            httpContext.RequestAborted);

        if (string.IsNullOrWhiteSpace(validAccessToken))
        {
            // Expired sessions without a usable refresh token must sign in again.
            // Clear the stale access token as well so Account/Login does not
            // redirect straight back to a protected page.
            session.Clear();
            context.Result = new RedirectToActionResult("Login", "Account", null);
            return;
        }

        await next();
    }
}
