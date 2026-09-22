using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
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

    public async Task<SupabaseAuthResult> SignUpAsync(string email, string password, string username, string phoneNumber, CancellationToken cancellationToken = default)
    {
        var response = await SendAsync("/auth/v1/signup", new
        {
            email,
            password,
            data = new
            {
                username,
                full_name = username,
                phone_number = $"+91{phoneNumber}"
            }
        }, cancellationToken);

        return await ReadResultAsync(response, requireAccessToken: false, cancellationToken);
    }

    public async Task<SupabaseAuthResult> LoginAsync(string email, string password, CancellationToken cancellationToken = default)
    {
        var response = await SendAsync("/auth/v1/token?grant_type=password", new { email, password }, cancellationToken);
        return await ReadResultAsync(response, requireAccessToken: true, cancellationToken);
    }

    public string GetGoogleLoginUrl(string redirectUri, string codeChallenge)
    {
        var baseUrl = Required("Supabase:Url").TrimEnd('/');
        return $"{baseUrl}/auth/v1/authorize?provider=google&redirect_to={Uri.EscapeDataString(redirectUri)}&code_challenge={Uri.EscapeDataString(codeChallenge)}&code_challenge_method=S256";
    }

    public async Task<SupabaseAuthResult> ExchangeGoogleCodeAsync(string code, string codeVerifier, CancellationToken cancellationToken = default)
    {
        var response = await SendAsync(
            "/auth/v1/token?grant_type=pkce",
            new { auth_code = code, code_verifier = codeVerifier },
            cancellationToken);

        return await ReadResultAsync(response, requireAccessToken: true, cancellationToken);
    }

    public static string CreateCodeVerifier()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        return Base64UrlEncode(bytes);
    }

    public static string CreateCodeChallenge(string codeVerifier)
    {
        var hash = SHA256.HashData(Encoding.ASCII.GetBytes(codeVerifier));
        return Base64UrlEncode(hash);
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
        string? userId = null;
        string? email = null;

        if (!string.IsNullOrWhiteSpace(body))
        {
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;

            accessToken = root.TryGetProperty("access_token", out var tokenElement)
                ? tokenElement.GetString()
                : null;

            if (root.TryGetProperty("user", out var userElement))
            {
                userId = userElement.TryGetProperty("id", out var idElement) ? idElement.GetString() : null;
                email = userElement.TryGetProperty("email", out var emailElement) ? emailElement.GetString() : null;
            }
        }

        if (requireAccessToken && string.IsNullOrWhiteSpace(accessToken))
            return new(false, Error: "Authentication response was incomplete.");

        return new(true, accessToken, userId, email);
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

    private static string Base64UrlEncode(byte[] bytes) =>
        Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');

    private string Required(string key) =>
        _configuration[key] ?? throw new InvalidOperationException($"Missing configuration: {key}");
}
