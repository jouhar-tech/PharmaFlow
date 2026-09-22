namespace PharmaFlow.Services;

public interface ISupabaseAuthService
{
    Task<bool> SendPhoneOtpAsync(string phoneNumber, CancellationToken cancellationToken = default);
    Task<SupabaseAuthResult> VerifyPhoneOtpAsync(string phoneNumber, string token, CancellationToken cancellationToken = default);
    string GetGoogleLoginUrl(string redirectUri);
}

public sealed record SupabaseAuthResult(bool Success, string? AccessToken = null, string? Error = null);
