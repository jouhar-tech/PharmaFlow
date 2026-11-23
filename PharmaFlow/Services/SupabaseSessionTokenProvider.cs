using System.Text;
using System.Text.Json;

namespace PharmaFlow.Services;

public interface ISupabaseSessionTokenProvider
{
    Task<string?> GetValidAccessTokenAsync(HttpContext httpContext, CancellationToken cancellationToken = default);
}

/// <summary>
/// Keeps a user's Supabase access token usable for authenticated MVC requests.
/// Refresh tokens are rotated by Supabase, so both token values are updated
/// together in the server-side session after every successful refresh.
/// </summary>
public sealed class SupabaseSessionTokenProvider : ISupabaseSessionTokenProvider
{
    private static readonly TimeSpan RefreshBeforeExpiry = TimeSpan.FromMinutes(2);

    private readonly ISupabaseAuthService _authService;
    private readonly ILogger<SupabaseSessionTokenProvider> _logger;

    public SupabaseSessionTokenProvider(
        ISupabaseAuthService authService,
        ILogger<SupabaseSessionTokenProvider> logger)
    {
        _authService = authService;
        _logger = logger;
    }

    public async Task<string?> GetValidAccessTokenAsync(
        HttpContext httpContext,
        CancellationToken cancellationToken = default)
    {
        var session = httpContext.Session;
        var accessToken = session.GetString("SupabaseAccessToken");
        var refreshToken = session.GetString("SupabaseRefreshToken");
        var expiresAt = GetTokenExpiry(accessToken);

        if (!string.IsNullOrWhiteSpace(accessToken) &&
            expiresAt.HasValue &&
            expiresAt.Value > DateTimeOffset.UtcNow.Add(RefreshBeforeExpiry))
        {
            return accessToken;
        }

        // Existing sessions created before refresh-token support have no refresh
        // token. Let a still-valid access token finish its remaining lifetime;
        // the user will be asked to sign in again after it expires.
        if (string.IsNullOrWhiteSpace(refreshToken))
        {
            return !string.IsNullOrWhiteSpace(accessToken) &&
                   expiresAt.HasValue &&
                   expiresAt.Value > DateTimeOffset.UtcNow
                ? accessToken
                : null;
        }

        try
        {
            var result = await _authService.RefreshSessionAsync(refreshToken, cancellationToken);

            if (!result.Success || string.IsNullOrWhiteSpace(result.AccessToken))
            {
                _logger.LogWarning(
                    "Supabase session refresh was rejected. The user may need to sign in again. Reason: {Reason}",
                    result.Error ?? "No access token was returned.");

                // Do not interrupt a request while the current access token is
                // still valid. The next request can retry the refresh.
                return !string.IsNullOrWhiteSpace(accessToken) &&
                       expiresAt.HasValue &&
                       expiresAt.Value > DateTimeOffset.UtcNow
                    ? accessToken
                    : null;
            }

            var newRefreshToken = result.RefreshToken;
            if (string.IsNullOrWhiteSpace(newRefreshToken))
            {
                _logger.LogWarning(
                    "Supabase returned a new access token without a refresh token. Keeping the previous refresh token.");
                newRefreshToken = refreshToken;
            }

            session.SetString("SupabaseAccessToken", result.AccessToken);
            session.SetString("SupabaseRefreshToken", newRefreshToken);

            _logger.LogInformation("Supabase access token was refreshed for the current session.");
            return result.AccessToken;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Supabase session refresh failed unexpectedly.");

            // A transient refresh-endpoint failure should not invalidate a token
            // that Supabase would still accept.
            return !string.IsNullOrWhiteSpace(accessToken) &&
                   expiresAt.HasValue &&
                   expiresAt.Value > DateTimeOffset.UtcNow
                ? accessToken
                : null;
        }
    }

    private static DateTimeOffset? GetTokenExpiry(string? jwt)
    {
        if (string.IsNullOrWhiteSpace(jwt))
            return null;

        var parts = jwt.Split('.');
        if (parts.Length != 3 || string.IsNullOrWhiteSpace(parts[1]))
            return null;

        try
        {
            var payload = parts[1]
                .Replace('-', '+')
                .Replace('_', '/');

            payload = payload.Length % 4 switch
            {
                2 => payload + "==",
                3 => payload + "=",
                _ => payload
            };

            var json = Encoding.UTF8.GetString(Convert.FromBase64String(payload));
            using var document = JsonDocument.Parse(json);

            if (!document.RootElement.TryGetProperty("exp", out var expElement) ||
                !expElement.TryGetInt64(out var expiresUnixSeconds))
            {
                return null;
            }

            return DateTimeOffset.FromUnixTimeSeconds(expiresUnixSeconds);
        }
        catch (FormatException)
        {
            return null;
        }
        catch (JsonException)
        {
            return null;
        }
        catch (ArgumentOutOfRangeException)
        {
            return null;
        }
    }
}
