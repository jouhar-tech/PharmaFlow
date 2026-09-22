namespace PharmaFlow.Services;

public interface ISupabaseAuthService
{
    Task<SupabaseAuthResult> SignUpAsync(string email, string password, string fullName, string phoneNumber, CancellationToken cancellationToken = default);
    Task<SupabaseAuthResult> LoginAsync(string email, string password, CancellationToken cancellationToken = default);
    string GetGoogleLoginUrl(string redirectUri);
}

public sealed record SupabaseAuthResult(
    bool Success,
    string? AccessToken = null,
    string? UserId = null,
    string? Error = null);
