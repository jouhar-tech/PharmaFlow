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

    public async Task<SupabaseAuthResult> LoginWithPhoneAsync(string phoneNumber, string password, CancellationToken cancellationToken = default)
    {
        var response = await SendAsync("/auth/v1/token?grant_type=password", new { phone = phoneNumber, password }, cancellationToken);
        return await ReadResultAsync(response, requireAccessToken: true, cancellationToken);
    }

    public string GetStaffLoginEmail(string loginIdentifier)
    {
        var normalized = loginIdentifier.Trim().ToLowerInvariant();
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalized))).ToLowerInvariant();
        return $"staff-{hash[..32]}@staff.pharmaflow.app";
    }

    public async Task<SupabaseAuthResult> CreateStaffLoginAsync(
        string loginIdentifier,
        string fullName,
        string username,
        string password,
        CancellationToken cancellationToken = default)
    {
        var serviceRoleKey = _configuration["Supabase:ServiceRoleKey"];
        if (string.IsNullOrWhiteSpace(serviceRoleKey))
            return new(false, Error: "Staff login setup is incomplete. Configure Supabase:ServiceRoleKey on the server.");

        var email = GetStaffLoginEmail(loginIdentifier);
        var baseUrl = Required("Supabase:Url").TrimEnd('/');
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl}/auth/v1/admin/users");
        request.Headers.Add("apikey", serviceRoleKey);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", serviceRoleKey);
        request.Content = JsonContent.Create(new
        {
            email,
            password,
            email_confirm = true,
            user_metadata = new { username, full_name = fullName, account_type = "staff" }
        });

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var result = await ReadResultAsync(response, requireAccessToken: false, cancellationToken);
        if (!result.Success)
            return result;

        return result with { Email = email };
    }

    public string GetGoogleLoginUrl(string redirectUri, string codeChallenge)
    {
        var baseUrl = Required("Supabase:Url").TrimEnd('/');
        return $"{baseUrl}/auth/v1/authorize?provider=google&redirect_to={Uri.EscapeDataString(redirectUri)}&code_challenge={Uri.EscapeDataString(codeChallenge)}&code_challenge_method=S256";
    }

    // Generate the PKCE verifier/challenge used by the Google OAuth flow.
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

    public async Task<SupabaseAuthResult> ExchangeGoogleCodeAsync(string code, string codeVerifier, CancellationToken cancellationToken = default)
    {
        var response = await SendAsync(
            "/auth/v1/token?grant_type=pkce",
            new { auth_code = code, code_verifier = codeVerifier },
            cancellationToken);

        return await ReadResultAsync(response, requireAccessToken: true, cancellationToken);
    }

    private async Task<SupabaseAuthResult> ReadResultAsync(HttpResponseMessage response, bool requireAccessToken, CancellationToken cancellationToken)
    {
        using var responseScope = response;
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

        try
        {
            if (!string.IsNullOrWhiteSpace(body))
            {
                using var document = JsonDocument.Parse(body);
                var root = document.RootElement;

                accessToken = root.TryGetProperty("access_token", out var tokenElement)
                    ? tokenElement.GetString()
                    : null;

                var userElement = root.TryGetProperty("user", out var nestedUser)
                    ? nestedUser
                    : root;

                userId = userElement.TryGetProperty("id", out var idElement) ? idElement.GetString() : null;
                email = userElement.TryGetProperty("email", out var emailElement) ? emailElement.GetString() : null;
            }
        }
        catch (JsonException)
        {
            return new(false, Error: "Authentication service returned an invalid response.");
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
