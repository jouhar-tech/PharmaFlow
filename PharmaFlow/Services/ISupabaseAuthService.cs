namespace PharmaFlow.Services;

public interface ISupabaseAuthService
{
    Task<SupabaseAuthResult> SignUpAsync(string email, string password, string username, string phoneNumber, CancellationToken cancellationToken = default);
    Task<SupabaseAuthResult> LoginAsync(string email, string password, CancellationToken cancellationToken = default);
    Task<SupabaseAuthResult> LoginWithPhoneAsync(string phoneNumber, string password, CancellationToken cancellationToken = default);
    Task<SupabaseAuthResult> SignUpStaffAsync(string? email, string? phoneNumber, string password, string fullName, CancellationToken cancellationToken = default);
    string GetGoogleLoginUrl(string redirectUri, string codeChallenge);
    Task<SupabaseAuthResult> ExchangeGoogleCodeAsync(string code, string codeVerifier, CancellationToken cancellationToken = default);
}

public sealed record SupabaseAuthResult(
    bool Success,
    string? AccessToken = null,
    string? UserId = null,
    string? Email = null,
    string? Error = null);
