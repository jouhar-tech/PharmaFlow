using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace PharmaFlow.Services;

public sealed class SupabaseStorageService : ISupabaseStorageService
{
    private const long MaxObjectBytes = 25L * 1024 * 1024;
    private static readonly HashSet<string> AllowedContentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "image/jpeg",
        "image/png",
        "image/webp",
        "application/pdf"
    };

    private readonly HttpClient _httpClient;
    private readonly IConfiguration _configuration;

    public SupabaseStorageService(HttpClient httpClient, IConfiguration configuration)
    {
        _httpClient = httpClient;
        _configuration = configuration;
    }

    public async Task UploadAsync(
        string objectPath,
        Stream content,
        string contentType,
        CancellationToken cancellationToken = default)
    {
        ValidateObjectPath(objectPath);
        ValidateContentType(contentType);

        if (content.CanSeek)
            content.Position = 0;

        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            BuildObjectUri(objectPath, authenticated: false));

        AddServerCredentials(request);
        request.Headers.TryAddWithoutValidation("x-upsert", "false");

        var streamContent = new StreamContent(content);
        streamContent.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        request.Content = streamContent;

        using var response = await _httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"Supabase Storage upload failed with HTTP {(int)response.StatusCode}.");
        }
    }

    public async Task<SupabaseStoredObject?> DownloadAsync(
        string objectPath,
        string? fallbackContentType = null,
        CancellationToken cancellationToken = default)
    {
        ValidateObjectPath(objectPath);

        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            BuildObjectUri(objectPath, authenticated: true));

        AddServerCredentials(request);

        using var response = await _httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);

        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            return null;

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"Supabase Storage download failed with HTTP {(int)response.StatusCode}.");
        }

        if (response.Content.Headers.ContentLength is > MaxObjectBytes)
            throw new InvalidOperationException("The stored invoice file exceeds the allowed size.");

        var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
        if (bytes.Length == 0 || bytes.LongLength > MaxObjectBytes)
            throw new InvalidOperationException("The stored invoice file is empty or exceeds the allowed size.");

        var responseContentType = response.Content.Headers.ContentType?.MediaType;
        var contentType = !string.IsNullOrWhiteSpace(responseContentType) &&
                          AllowedContentTypes.Contains(responseContentType)
            ? responseContentType
            : fallbackContentType;

        if (string.IsNullOrWhiteSpace(contentType) || !AllowedContentTypes.Contains(contentType))
            contentType = GetContentTypeFromPath(objectPath);

        return new SupabaseStoredObject(bytes, contentType);
    }

    public async Task DeleteAsync(
        string objectPath,
        CancellationToken cancellationToken = default)
    {
        ValidateObjectPath(objectPath);

        var bucket = GetBucketName();
        var baseUrl = GetBaseUrl();
        using var request = new HttpRequestMessage(
            HttpMethod.Delete,
            $"{baseUrl}/storage/v1/object/{Uri.EscapeDataString(bucket)}");

        AddServerCredentials(request);
        request.Content = JsonContent.Create(new { prefixes = new[] { objectPath } });

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode &&
            response.StatusCode != System.Net.HttpStatusCode.NotFound)
        {
            throw new InvalidOperationException(
                $"Supabase Storage delete failed with HTTP {(int)response.StatusCode}.");
        }
    }

    private Uri BuildObjectUri(string objectPath, bool authenticated)
    {
        var bucket = Uri.EscapeDataString(GetBucketName());
        var escapedPath = string.Join(
            "/",
            objectPath.Split('/').Select(Uri.EscapeDataString));

        var accessSegment = authenticated ? "/authenticated" : string.Empty;
        return new Uri($"{GetBaseUrl()}/storage/v1/object{accessSegment}/{bucket}/{escapedPath}");
    }

    private void AddServerCredentials(HttpRequestMessage request)
    {
        var key = _configuration["Supabase:ServiceRoleKey"];
        if (string.IsNullOrWhiteSpace(key))
        {
            throw new InvalidOperationException(
                "Invoice image storage is not configured. Set Supabase:ServiceRoleKey on the server.");
        }

        request.Headers.Add("apikey", key);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
    }

    private string GetBaseUrl()
    {
        var value = _configuration["Supabase:Url"];
        if (string.IsNullOrWhiteSpace(value) ||
            !Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttps && !_configuration.GetValue<bool>("Supabase:AllowInsecureStorageUrl")))
        {
            throw new InvalidOperationException("Supabase:Url is missing or invalid for invoice image storage.");
        }

        return uri.ToString().TrimEnd('/');
    }

    private string GetBucketName()
    {
        var bucket = _configuration["Supabase:InvoiceStorageBucket"];
        return string.IsNullOrWhiteSpace(bucket) ? "invoice-scans" : bucket.Trim();
    }

    private static void ValidateObjectPath(string objectPath)
    {
        if (string.IsNullOrWhiteSpace(objectPath) ||
            objectPath.StartsWith("/", StringComparison.Ordinal) ||
            objectPath.Contains('\\') ||
            objectPath.Split('/').Any(segment =>
                string.IsNullOrWhiteSpace(segment) || segment is "." or ".."))
        {
            throw new ArgumentException("Invalid invoice storage path.", nameof(objectPath));
        }
    }

    private static void ValidateContentType(string contentType)
    {
        if (!AllowedContentTypes.Contains(contentType))
            throw new ArgumentException("Unsupported invoice content type.", nameof(contentType));
    }

    private static string GetContentTypeFromPath(string objectPath) =>
        Path.GetExtension(objectPath).ToLowerInvariant() switch
        {
            ".jpg" or ".jpeg" => "image/jpeg",
            ".png" => "image/png",
            ".webp" => "image/webp",
            ".pdf" => "application/pdf",
            _ => "application/octet-stream"
        };
}
