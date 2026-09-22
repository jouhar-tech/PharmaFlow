using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace PharmaFlow.Services;

public sealed class SupabaseAuthService : ISupabaseAuthService
{
    private readonly HttpClient _httpClient;
    private readonly IConfiguration _configuration;

    public SupabaseAuthService(HttpClient httpClient, IConfiguration configuration)
    {
        _httpClient = httpClient;
        _configuration = configuration;
    }

    public async Task<bool> SendPhoneOtpAsync(string phoneNumber, CancellationToken cancellationToken = default)
    {
        var response = await SendAsync("/auth/v1/otp", new { phone = $"+91{phoneNumber}" }, cancellationToken);
        return response.IsSuccessStatusCode;
    }

    public async Task<SupabaseAuthResult> VerifyPhoneOtpAsync(string phoneNumber, string token, CancellationToken cancellationToken = default)
    {
        var response = await SendAsync("/auth/v1/verify", new
        {
            phone = $"+91{phoneNumber}",
            token,
            type = "sms"
        }, cancellationToken);

        if (!response.IsSuccessStatusCode)
            return new(false, Error: await response.Content.ReadAsStringAsync(cancellationToken));

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        var accessToken = document.RootElement.TryGetProperty("access_token", out var tokenElement)
            ? tokenElement.GetString()
            : null;

        return new(accessToken is not null, accessToken, accessToken is null ? "Verification failed." : null);
    }

    public string GetGoogleLoginUrl(string redirectUri)
    {
        var baseUrl = Required("Supabase:Url").TrimEnd('/');
        return $"{baseUrl}/auth/v1/authorize?provider=google&redirect_to={Uri.EscapeDataString(redirectUri)}";
    }

    private async Task<HttpResponseMessage> SendAsync(string path, object body, CancellationToken cancellationToken)
    {
        var baseUrl = Required("Supabase:Url").TrimEnd('/');
        var key = Required("Supabase:AnonKey");
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl}{path}");
        request.Headers.Add("apikey", key);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
        request.Content = JsonContent.Create(body);
        return await _httpClient.SendAsync(request, cancellationToken);
    }

    private string Required(string key) =>
        _configuration[key] ?? throw new InvalidOperationException($"Missing configuration: {key}");
}
