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

        if (string.IsNullOrWhiteSpace(accessToken) ||
            string.IsNullOrWhiteSpace(userId) ||
            !Guid.TryParse(userId, out _) ||
            !long.TryParse(profileId, out _))
        {
            context.Result = new RedirectToActionResult("Login", "Account", null);
            return;
        }

        await next();
    }
}
