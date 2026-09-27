using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PharmaFlow.Services;

public interface IInvoiceVisionService
{
    Task<IReadOnlyList<InvoiceVisionItem>> ExtractAsync(
        Stream fileStream,
        string mimeType,
        CancellationToken cancellationToken);
}

public sealed class GeminiInvoiceVisionService : IInvoiceVisionService
{
    private const string DefaultModel = "gemini-3.8-flash";
    private const string EndpointTemplate = "https://generativelanguage.googleapis.com/v1beta/models/{0}:generateContent";
    private const int MaxFileBytes = 25 * 1024 * 1024;

    private readonly HttpClient _httpClient;
    private readonly IConfiguration _configuration;
    private readonly ILogger<GeminiInvoiceVisionService> _logger;

    public GeminiInvoiceVisionService(
        HttpClient httpClient,
        IConfiguration configuration,
        ILogger<GeminiInvoiceVisionService> logger)
    {
        _httpClient = httpClient;
        _configuration = configuration;
        _logger = logger;
        _httpClient.Timeout = TimeSpan.FromMinutes(2);
    }

    public async Task<IReadOnlyList<InvoiceVisionItem>> ExtractAsync(
        Stream fileStream,
        string mimeType,
        CancellationToken cancellationToken)
    {
        var apiKey =
            _configuration["Gemini:ApiKey"] ??
            Environment.GetEnvironmentVariable("GEMINI_API_KEY");

        if (string.IsNullOrWhiteSpace(apiKey))
            throw new InvalidOperationException("Gemini API key is not configured.");

        if (fileStream is null || !fileStream.CanRead)
            throw new InvalidOperationException("The invoice file could not be read.");

        using var memory = new MemoryStream();
        await fileStream.CopyToAsync(memory, cancellationToken);

        if (memory.Length == 0)
            throw new InvalidOperationException("The invoice file is empty.");

        if (memory.Length > MaxFileBytes)
            throw new InvalidOperationException("The invoice file is too large. Please use a file smaller than 25 MB.");

        var fileData = Convert.ToBase64String(memory.ToArray());
        var model = _configuration["Gemini:Model"]?.Trim();
        if (string.IsNullOrWhiteSpace(model))
            model = DefaultModel;

        var schema = new
        {
            type = "object",
            properties = new
            {
                items = new
                {
                    type = "array",
                    items = new
                    {
                        type = "object",
                        properties = new
                        {
                            productName = new
                            {
                                type = "string",
                                description = "Exact product/item description printed in the stock table. Do not invent or silently correct it."
                            },
                            batchNumber = new
                            {
                                type = "string",
                                description = "Batch or lot number from the batch column. Empty string if unreadable."
                            },
                            expiryDate = new
                            {
                                type = "string",
                                description = "Expiry date as YYYY-MM-DD. For MM/YY or MM-YY invoices, use the last calendar day of that month. Empty string if unreadable."
                            },
                            quantity = new
                            {
                                type = "number",
                                description = "Billed quantity only, not free quantity. Use 0 when unreadable."
                            },
                            confidence = new
                            {
                                type = "number",
                                description = "Your confidence for this row from 0 to 100."
                            }
                        },
                        required = new[] { "productName", "batchNumber", "expiryDate", "quantity", "confidence" }
                    }
                }
            },
            required = new[] { "items" }
        };

        var prompt = """
Read this pharmacy purchase invoice visually and extract the STOCK TABLE only.

Return one item for every genuine stock/product row in the invoice.

The stock table typically contains columns similar to:
Sl No, HSN/SAC, Rack No, Item Description, Quantity/Billed, Free, Pack, Batch, Exp Date, MRP, Trade Price, Discount, Taxable Value, GST.

For EVERY row, extract ONLY:
1. Product Name
2. Batch Number
3. Expiry Date
4. Billed Quantity
5. Confidence

Rules:
- Use the visual table structure, not only OCR text.
- Product Name must contain the item description only. Do not include HSN/SAC, rack number, prices, discount, tax, totals, or footer notices.
- Preserve the product wording visible on the invoice. Do not invent missing medicine names or silently substitute a different medicine.
- Quantity means the Billed Quantity column, NOT Free quantity.
- Batch must come from the Batch column. Do not use HSN, rack, price, invoice number or GST number as the batch.
- Expiry may be MM/YY, MM-YY, MM/YYYY, DD/MM/YYYY or similar. Convert it to YYYY-MM-DD. For a month-only expiry such as 05-29, use the last day of that month (2029-05-31).
- If any required field cannot be read confidently, return an empty string for text fields or 0 for quantity and lower the row confidence. Do not guess.
- Ignore headers, supplier/customer details, invoice metadata, payment details, notices, tax summaries, grand totals, and footer text.
- Do not merge two different product rows.
- Return JSON that exactly matches the supplied schema.
""";

        object inputDocument;
        if (string.Equals(mimeType, "application/pdf", StringComparison.OrdinalIgnoreCase))
        {
            inputDocument = new
            {
                type = "document",
                data = fileData,
                mime_type = mimeType
            };
        }
        else
        {
            inputDocument = new
            {
                type = "image",
                data = fileData,
                mime_type = mimeType
            };
        }

        var payload = new
        {
            contents = new[]
            {
                new
                {
                    role = "user",
                    parts = new object[]
                    {
                        new { text = prompt },
                        new
                        {
                            inline_data = new
                            {
                                mime_type = mimeType,
                                data = fileData
                            }
                        }
                    }
                }
            },
            generationConfig = new
            {
                responseMimeType = "application/json",
                responseSchema = schema,
                thinkingConfig = new
                {
                    thinkingLevel = "medium"
                }
            }
        };

        var endpoint = string.Format(
            CultureInfo.InvariantCulture,
            EndpointTemplate,
            Uri.EscapeDataString(model));

        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
        request.Headers.Add("x-goog-api-key", apiKey);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Content = new StringContent(
            JsonSerializer.Serialize(payload),
            Encoding.UTF8,
            "application/json");

        using var response = await _httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);

        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning(
                "Gemini invoice extraction failed with status {StatusCode}. Response: {Response}",
                response.StatusCode,
                responseBody.Length > 2000 ? responseBody[..2000] : responseBody);

            var apiDetail = TryExtractGeminiError(responseBody);

            var detail = response.StatusCode switch
            {
                System.Net.HttpStatusCode.Unauthorized or
                System.Net.HttpStatusCode.Forbidden =>
                    "Gemini API authentication failed. Check the API key and project permissions.",
                System.Net.HttpStatusCode.TooManyRequests =>
                    "Gemini API rate limit reached. Please wait a moment and try again.",
                System.Net.HttpStatusCode.BadRequest =>
                    string.IsNullOrWhiteSpace(apiDetail)
                        ? "Gemini rejected the invoice request. Check the Gemini API configuration and try again."
                        : $"Gemini rejected the invoice request: {apiDetail}",
                _ =>
                    string.IsNullOrWhiteSpace(apiDetail)
                        ? "The invoice could not be analyzed by the AI service. Please try again."
                        : $"The AI service returned an error: {apiDetail}"
            };

            throw new InvalidOperationException(detail);
        }

        var outputText = ExtractOutputText(responseBody);
        if (string.IsNullOrWhiteSpace(outputText))
            throw new InvalidOperationException("The AI service returned no invoice data.");

        GeminiInvoiceResponse? extracted;
        try
        {
            extracted = JsonSerializer.Deserialize<GeminiInvoiceResponse>(
                outputText,
                new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Gemini returned invalid structured invoice JSON.");
            throw new InvalidOperationException("The AI service returned unreadable invoice data.");
        }

        if (extracted?.Items is null)
            throw new InvalidOperationException("The AI service returned no invoice rows.");

        return extracted.Items
            .Take(200)
            .Select((item, index) => NormalizeItem(item, index + 1))
            .Where(item =>
                !string.IsNullOrWhiteSpace(item.ProductName) ||
                !string.IsNullOrWhiteSpace(item.BatchNumber) ||
                !string.IsNullOrWhiteSpace(item.ExpiryDateText) ||
                item.Quantity > 0m)
            .ToList();
    }

    private static InvoiceVisionItem NormalizeItem(
        GeminiInvoiceItem item,
        int rowNumber)
    {
        var product = Clean(item.ProductName, 200);
        var batch = Clean(item.BatchNumber, 100);
        var expiryText = Clean(item.ExpiryDate, 20);
        var confidence = Math.Clamp(item.Confidence, 0m, 100m);
        var quantity = item.Quantity > 0m ? Math.Min(item.Quantity, 1_000_000m) : 0m;

        return new InvoiceVisionItem(
            rowNumber,
            product,
            batch,
            expiryText,
            quantity,
            confidence);
    }

    private static string Clean(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var cleaned = System.Text.RegularExpressions.Regex.Replace(
            value.Trim(),
            @"\s+",
            " ");

        return cleaned.Length <= maxLength
            ? cleaned
            : cleaned[..maxLength];
    }

    private static string? ExtractOutputText(string responseBody)
    {
        using var document = JsonDocument.Parse(responseBody);
        var root = document.RootElement;

        if (root.TryGetProperty("candidates", out var candidates) &&
            candidates.ValueKind == JsonValueKind.Array)
        {
            foreach (var candidate in candidates.EnumerateArray())
            {
                if (!candidate.TryGetProperty("content", out var content) ||
                    !content.TryGetProperty("parts", out var parts) ||
                    parts.ValueKind != JsonValueKind.Array)
                    continue;

                foreach (var part in parts.EnumerateArray())
                {
                    if (part.TryGetProperty("text", out var text) &&
                        text.ValueKind == JsonValueKind.String)
                    {
                        return text.GetString();
                    }
                }
            }
        }

        return null;
    }

    private static string? TryExtractGeminiError(string responseBody)
    {
        try
        {
            using var document = JsonDocument.Parse(responseBody);
            var root = document.RootElement;

            if (root.TryGetProperty("error", out var error) &&
                error.TryGetProperty("message", out var message) &&
                message.ValueKind == JsonValueKind.String)
            {
                return message.GetString();
            }
        }
        catch (JsonException)
        {
            // Keep the user-facing message generic if Gemini returned non-JSON.
        }

        return null;
    }

    private sealed class GeminiInvoiceResponse
    {
        [JsonPropertyName("items")]
        public List<GeminiInvoiceItem> Items { get; set; } = [];
    }

    private sealed class GeminiInvoiceItem
    {
        [JsonPropertyName("productName")]
        public string? ProductName { get; set; }

        [JsonPropertyName("batchNumber")]
        public string? BatchNumber { get; set; }

        [JsonPropertyName("expiryDate")]
        public string? ExpiryDate { get; set; }

        [JsonPropertyName("quantity")]
        public decimal Quantity { get; set; }

        [JsonPropertyName("confidence")]
        public decimal Confidence { get; set; }
    }
}

public sealed record InvoiceVisionItem(
    int RowNumber,
    string ProductName,
    string BatchNumber,
    string ExpiryDateText,
    decimal Quantity,
    decimal Confidence);
