using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace PharmaFlow.Filters;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class SessionAuthorizeAttribute : Attribute, IAsyncActionFilter
{
    public async Task OnActionExecutionAsync(
        ActionExecutingContext context,
        ActionExecutionDelegate next)
    {
        var session = context.HttpContext.Session;
        var accessToken = session.GetString("SupabaseAccessToken");
        var userId = session.GetString("SupabaseUserId");
        var profileId = session.GetString("ProfileId");
        var controller = context.RouteData.Values["controller"]?.ToString();
        var action = context.RouteData.Values["action"]?.ToString();

        // Business setup must remain reachable, but no other authenticated app
        // page should be accessible until the owner has saved a business name.
        var isBusinessSetup =
            string.Equals(controller, "Business", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(action, "Setup", StringComparison.OrdinalIgnoreCase);

        if (string.IsNullOrWhiteSpace(accessToken) ||
            string.IsNullOrWhiteSpace(userId) ||
            !Guid.TryParse(userId, out _) ||
            !long.TryParse(profileId, out _))
        {
            context.Result = new RedirectToActionResult("Login", "Account", null);
            return;
        }

        if (!isBusinessSetup && string.IsNullOrWhiteSpace(session.GetString("BusinessName")))
        {
            context.Result = new RedirectToActionResult("Setup", "Business", null);
            return;
        }

        await next();
    }
}
