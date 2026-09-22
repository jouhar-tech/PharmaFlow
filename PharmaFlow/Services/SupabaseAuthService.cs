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

    public async Task<SupabaseAuthResult> SignUpAsync(string email, string password, string fullName, string phoneNumber, CancellationToken cancellationToken = default)
    {
        var response = await SendAsync("/auth/v1/signup", new
        {
            email,
            password,
            data = new { full_name = fullName, phone_number = $"+91{phoneNumber}" }
        }, cancellationToken);

        return await ReadResultAsync(response, requireAccessToken: false, cancellationToken);
    }

    public async Task<SupabaseAuthResult> LoginAsync(string email, string password, CancellationToken cancellationToken = default)
    {
        var response = await SendAsync("/auth/v1/token?grant_type=password", new { email, password }, cancellationToken);
        return await ReadResultAsync(response, requireAccessToken: true, cancellationToken);
    }

    public string GetGoogleLoginUrl(string redirectUri)
    {
        var baseUrl = Required("Supabase:Url").TrimEnd('/');
        return $"{baseUrl}/auth/v1/authorize?provider=google&redirect_to={Uri.EscapeDataString(redirectUri)}";
    }

    private async Task<SupabaseAuthResult> ReadResultAsync(HttpResponseMessage response, bool requireAccessToken, CancellationToken cancellationToken)
    {
        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            try
            {
                using var errorDocument = JsonDocument.Parse(body);
                var message = errorDocument.RootElement.TryGetProperty("msg", out var msg) ? msg.GetString()
                    : errorDocument.RootElement.TryGetProperty("message", out var errorMessage) ? errorMessage.GetString()
                    : "Authentication request failed.";
                return new(false, Error: message);
            }
            catch (JsonException)
            {
                return new(false, Error: "Authentication request failed.");
            }
        }

        string? accessToken = null;
        if (!string.IsNullOrWhiteSpace(body))
        {
            using var document = JsonDocument.Parse(body);
            accessToken = document.RootElement.TryGetProperty("access_token", out var tokenElement)
                ? tokenElement.GetString()
                : null;
        }

        if (requireAccessToken && string.IsNullOrWhiteSpace(accessToken))
            return new(false, Error: "Authentication response was incomplete.");

        return new(true, accessToken);
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
