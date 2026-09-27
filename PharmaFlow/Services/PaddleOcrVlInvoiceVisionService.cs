using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PharmaFlow.Services;

public sealed class PaddleOcrVlInvoiceVisionService
{
    private const long MaxFileBytes = 25L * 1024 * 1024;

    private readonly HttpClient _httpClient;
    private readonly IConfiguration _configuration;
    private readonly ILogger<PaddleOcrVlInvoiceVisionService> _logger;

    public PaddleOcrVlInvoiceVisionService(
        HttpClient httpClient,
        IConfiguration configuration,
        ILogger<PaddleOcrVlInvoiceVisionService> logger)
    {
        _httpClient = httpClient;
        _configuration = configuration;
        _logger = logger;

        var timeoutSeconds = _configuration.GetValue("PaddleOCR:TimeoutSeconds", 300);
        _httpClient.Timeout = TimeSpan.FromSeconds(Math.Clamp(timeoutSeconds, 30, 900));
    }

    public async Task<IReadOnlyList<InvoiceVisionItem>> ExtractAsync(
        Stream fileStream,
        string mimeType,
        CancellationToken cancellationToken)
    {
        if (!IsEnabled())
            throw new InvalidOperationException("PaddleOCR local service is disabled.");

        if (fileStream is null || !fileStream.CanRead)
            throw new InvalidOperationException("The invoice file could not be read.");

        using var memory = new MemoryStream();
        await fileStream.CopyToAsync(memory, cancellationToken);

        if (memory.Length == 0)
            throw new InvalidOperationException("The invoice file is empty.");

        if (memory.Length > MaxFileBytes)
            throw new InvalidOperationException("The invoice file is too large. Please use a file smaller than 25 MB.");

        var baseUrl = _configuration["PaddleOCR:BaseUrl"]?.Trim();
        if (string.IsNullOrWhiteSpace(baseUrl))
            throw new InvalidOperationException("PaddleOCR local service URL is not configured.");

        var endpoint = new Uri(new Uri(baseUrl.TrimEnd('/') + "/"), "extract");

        using var form = new MultipartFormDataContent();
        using var content = new ByteArrayContent(memory.ToArray());
        content.Headers.ContentType = new MediaTypeHeaderValue(
            string.IsNullOrWhiteSpace(mimeType) ? "application/octet-stream" : mimeType);

        form.Add(content, "invoice", "invoice" + ExtensionForMime(mimeType));

        var apiKey = _configuration["PaddleOCR:ApiKey"]?.Trim();
        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            Content = form
        };

        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        if (!string.IsNullOrWhiteSpace(apiKey))
            request.Headers.TryAddWithoutValidation("X-PaddleOCR-Api-Key", apiKey);

        using var response = await _httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);

        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning(
                "PaddleOCR local invoice service returned {StatusCode}: {Response}",
                (int)response.StatusCode,
                responseBody.Length > 2000 ? responseBody[..2000] : responseBody);

            var detail = TryGetErrorMessage(responseBody);
            throw new InvalidOperationException(
                string.IsNullOrWhiteSpace(detail)
                    ? $"PaddleOCR local service returned HTTP {(int)response.StatusCode}."
                    : $"PaddleOCR local service error: {detail}");
        }

        PaddleOcrResponse? payload;
        try
        {
            payload = JsonSerializer.Deserialize<PaddleOcrResponse>(
                responseBody,
                new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "PaddleOCR local service returned invalid JSON.");
            throw new InvalidOperationException("PaddleOCR returned an unreadable response.");
        }

        if (payload is null || !payload.Success)
            throw new InvalidOperationException(
                string.IsNullOrWhiteSpace(payload?.Message)
                    ? "PaddleOCR did not complete invoice extraction."
                    : payload.Message);

        if (string.IsNullOrWhiteSpace(payload.Markdown))
            throw new InvalidOperationException("PaddleOCR returned no document text.");

        var items = PaddleOcrMarkdownInvoiceParser.Parse(payload.Markdown);

        if (items.Count == 0)
            throw new InvalidOperationException(
                "PaddleOCR could not identify usable stock rows in the invoice table.");

        _logger.LogInformation(
            "PaddleOCR-VL extracted {ItemCount} invoice rows from {PageCount} page(s).",
            items.Count,
            payload.PageCount);

        return items;
    }

    private bool IsEnabled() =>
        _configuration.GetValue("PaddleOCR:Enabled", true);

    private static string ExtensionForMime(string? mimeType) =>
        mimeType?.ToLowerInvariant() switch
        {
            "application/pdf" => ".pdf",
            "image/png" => ".png",
            "image/webp" => ".webp",
            _ => ".jpg"
        };

    private static string? TryGetErrorMessage(string responseBody)
    {
        try
        {
            using var document = JsonDocument.Parse(responseBody);
            if (document.RootElement.TryGetProperty("message", out var message) &&
                message.ValueKind == JsonValueKind.String)
            {
                return message.GetString();
            }
        }
        catch (JsonException)
        {
            // Keep the calling layer responsible for the fallback.
        }

        return null;
    }

    private sealed class PaddleOcrResponse
    {
        [JsonPropertyName("success")]
        public bool Success { get; set; }

        [JsonPropertyName("message")]
        public string? Message { get; set; }

        [JsonPropertyName("markdown")]
        public string? Markdown { get; set; }

        [JsonPropertyName("pageCount")]
        public int PageCount { get; set; }
    }
}
