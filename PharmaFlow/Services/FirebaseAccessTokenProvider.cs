using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Caching.Memory;

namespace PharmaFlow.Services;

/// <summary>
/// Creates and caches short-lived OAuth 2.0 access tokens for Firebase Cloud Messaging HTTP v1.
/// Credentials are supplied only through server-side configuration/environment variables.
/// </summary>
public sealed class FirebaseAccessTokenProvider
{
    private const string FirebaseMessagingScope = "https://www.googleapis.com/auth/firebase.messaging";
    private const string TokenEndpoint = "https://oauth2.googleapis.com/token";
    private const string CacheKey = "pharmahex-firebase-fcm-access-token";

    private readonly IConfiguration _configuration;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IMemoryCache _memoryCache;
    private readonly ILogger<FirebaseAccessTokenProvider> _logger;
    private readonly SemaphoreSlim _refreshLock = new(1, 1);

    public FirebaseAccessTokenProvider(
        IConfiguration configuration,
        IHttpClientFactory httpClientFactory,
        IMemoryCache memoryCache,
        ILogger<FirebaseAccessTokenProvider> logger)
    {
        _configuration = configuration;
        _httpClientFactory = httpClientFactory;
        _memoryCache = memoryCache;
        _logger = logger;
    }

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(_configuration["Firebase:ProjectId"]) &&
        !string.IsNullOrWhiteSpace(_configuration["Firebase:ClientEmail"]) &&
        !string.IsNullOrWhiteSpace(_configuration["Firebase:PrivateKey"]);

    public async Task<string?> GetAccessTokenAsync(CancellationToken cancellationToken)
    {
        if (_memoryCache.TryGetValue(CacheKey, out string? cachedToken) &&
            !string.IsNullOrWhiteSpace(cachedToken))
        {
            return cachedToken;
        }

        if (!IsConfigured)
        {
            _logger.LogWarning(
                "Firebase FCM sending is not configured. Set Firebase:ProjectId, Firebase:ClientEmail and Firebase:PrivateKey on the server.");
            return null;
        }

        await _refreshLock.WaitAsync(cancellationToken);
        try
        {
            if (_memoryCache.TryGetValue(CacheKey, out cachedToken) &&
                !string.IsNullOrWhiteSpace(cachedToken))
            {
                return cachedToken;
            }

            var clientEmail = _configuration["Firebase:ClientEmail"]!.Trim();
            var privateKey = _configuration["Firebase:PrivateKey"]!
                .Replace("\\n", "\n", StringComparison.Ordinal)
                .Replace("\r\n", "\n", StringComparison.Ordinal)
                .Trim();

            var issuedAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var jwtHeader = Base64UrlEncode(JsonSerializer.SerializeToUtf8Bytes(new
            {
                alg = "RS256",
                typ = "JWT"
            }));
            var jwtClaims = Base64UrlEncode(JsonSerializer.SerializeToUtf8Bytes(new
            {
                iss = clientEmail,
                scope = FirebaseMessagingScope,
                aud = TokenEndpoint,
                iat = issuedAt,
                exp = issuedAt + 3600
            }));

            var unsignedAssertion = $"{jwtHeader}.{jwtClaims}";
            using var rsa = RSA.Create();
            rsa.ImportFromPem(privateKey);
            var signature = rsa.SignData(
                Encoding.ASCII.GetBytes(unsignedAssertion),
                HashAlgorithmName.SHA256,
                RSASignaturePadding.Pkcs1);

            var assertion = $"{unsignedAssertion}.{Base64UrlEncode(signature)}";
            using var request = new HttpRequestMessage(HttpMethod.Post, TokenEndpoint)
            {
                Content = new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["grant_type"] = "urn:ietf:params:oauth:grant-type:jwt-bearer",
                    ["assertion"] = assertion
                })
            };

            var client = _httpClientFactory.CreateClient("FirebaseOAuth");
            using var response = await client.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);

            var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError(
                    "Firebase OAuth token request failed. HTTP {StatusCode}; response: {Response}",
                    (int)response.StatusCode,
                    responseBody.Length > 1200 ? responseBody[..1200] : responseBody);
                return null;
            }

            var tokenResponse = JsonSerializer.Deserialize<GoogleAccessTokenResponse>(
                responseBody,
                new JsonSerializerOptions(JsonSerializerDefaults.Web));

            if (string.IsNullOrWhiteSpace(tokenResponse?.AccessToken))
            {
                _logger.LogError("Firebase OAuth token response did not contain an access token.");
                return null;
            }

            var cacheSeconds = Math.Max(60, tokenResponse.ExpiresIn - 300);
            _memoryCache.Set(
                CacheKey,
                tokenResponse.AccessToken,
                TimeSpan.FromSeconds(cacheSeconds));

            return tokenResponse.AccessToken;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Could not create a Firebase FCM OAuth access token.");
            return null;
        }
        finally
        {
            _refreshLock.Release();
        }
    }

    private static string Base64UrlEncode(byte[] value) =>
        Convert.ToBase64String(value)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');

    private sealed class GoogleAccessTokenResponse
    {
        [JsonPropertyName("access_token")]
        public string? AccessToken { get; set; }

        [JsonPropertyName("expires_in")]
        public int ExpiresIn { get; set; }
    }
}
