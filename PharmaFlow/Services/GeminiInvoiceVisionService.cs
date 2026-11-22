using System.Diagnostics;
using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PharmaFlow.Services;

public interface IInvoiceVisionService
{
    Task<InvoiceVisionResult> ExtractAsync(
        Stream fileStream,
        string mimeType,
        CancellationToken cancellationToken);
}

public sealed class GeminiInvoiceVisionService : IInvoiceVisionService
{
    private const string DefaultModel = "gemini-3.1-flash-lite";
    private const string DefaultFallbackModel = "gemini-3.5-flash-lite";
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
        // Apply the bounded timeout below to both response headers and body.
        _httpClient.Timeout = System.Threading.Timeout.InfiniteTimeSpan;
    }

    public async Task<InvoiceVisionResult> ExtractAsync(
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
        var primaryModel = _configuration["Gemini:Model"]?.Trim();
        if (string.IsNullOrWhiteSpace(primaryModel))
            primaryModel = DefaultModel;

        var fallbackModel = _configuration["Gemini:FallbackModel"]?.Trim();
        if (string.IsNullOrWhiteSpace(fallbackModel))
            fallbackModel = DefaultFallbackModel;

        var schema = new
        {
            type = "object",
            properties = new
            {
                distributorName = new
                {
                    type = "string",
                    description = "Distributor/supplier company name printed on the invoice. Preserve the legal/trading name exactly as visible. Empty string if not readable."
                },
                invoiceNumber = new
                {
                    type = "string",
                    description = "Supplier invoice/bill number. Do not use GSTIN, phone number, order number or customer reference. Empty string if not readable."
                },
                invoiceDate = new
                {
                    type = "string",
                    description = "Invoice date as YYYY-MM-DD. Empty string if not readable."
                },
                totalAmount = new
                {
                    type = "number",
                    description = "Final invoice grand total/payable amount printed on the invoice. Use 0 when unreadable."
                },
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
                            purchaseUnitPrice = new
                            {
                                type = "number",
                                description = "Net purchase rate per billed unit. Prefer explicit net/trade/purchase rate. If only net line amount and billed quantity are visible, divide that amount by billed quantity. Never use MRP or whole invoice total. Use 0 when unavailable."
                            },
                            mrp = new
                            {
                                type = "number",
                                description = "Maximum Retail Price / MRP printed for this product row. Use 0 when unreadable."
                            },
                            confidence = new
                            {
                                type = "number",
                                description = "Your confidence for this row from 0 to 100."
                            }
                        },
                        required = new[] { "productName", "batchNumber", "expiryDate", "quantity", "purchaseUnitPrice", "mrp", "confidence" }
                    }
                }
            },
            required = new[] { "distributorName", "invoiceNumber", "invoiceDate", "totalAmount", "items" }
        };

        var prompt = """
Read this pharmacy purchase invoice visually and extract the invoice header metadata and STOCK TABLE.

Return the distributor/company name, supplier invoice number, invoice date, final invoice grand total, and one item for every genuine stock/product row in the invoice.

The stock table typically contains columns similar to:
Sl No, HSN/SAC, Rack No, Item Description, Quantity/Billed, Free, Pack, Batch, Exp Date, MRP, Trade Price, Discount, Taxable Value, GST.

For EVERY row, extract:
1. Product Name
2. Batch Number
3. Expiry Date
4. Billed Quantity
5. Purchase Unit Price
6. MRP
7. Confidence

Rules:
- Use the visual table structure, not only OCR text.
- Product Name must contain the item description only. Do not include HSN/SAC, rack number, prices, discount, tax, totals, or footer notices.
- Preserve the product wording visible on the invoice. Do not invent missing medicine names or silently substitute a different medicine.
- Quantity means the Billed Quantity column, NOT Free quantity.
- Purchase Unit Price means the net purchase rate per billed unit paid by the pharmacy. Prefer an explicit net/trade/purchase rate (for example Rate, Trade Price, PTR, PTS or Net Rate) when clearly shown.
- If the invoice shows only a net line amount and billed quantity, divide the net line amount by billed quantity to obtain purchase unit price.
- Do not use MRP as Purchase Unit Price. Do not use the whole invoice total or a tax total as a unit price. Use 0 when a reliable purchase unit price is not visible.
- Batch must come from the Batch column. Do not use HSN, rack, price, invoice number or GST number as the batch.
- Expiry may be MM/YY, MM-YY, MM/YYYY, DD/MM/YYYY or similar. Convert it to YYYY-MM-DD. For a month-only expiry such as 05-29, use the last day of that month (2029-05-31).
- If any required field cannot be read confidently, return an empty string for text fields or 0 for quantity and lower the row confidence. Do not guess.
- For metadata, identify the distributor/supplier name, supplier invoice number, invoice date, and final grand total from the invoice header/footer.
- Do not confuse distributor name with customer/pharmacy name.
- The invoice number must be the supplier's printed invoice/bill number, not an order number or GSTIN.
- The totalAmount must be the final invoice grand total/payable amount, not a line MRP total or tax subtotal.
- For each stock row, extract the MRP and purchase unit price columns when present. Prefer the actual net purchase rate; do not substitute MRP for it.
- Ignore customer details, notices, and unrelated footer text. The distributor name, invoice number, invoice date, and grand total are required invoice-level metadata.
- Do not merge two different product rows.
- Return JSON that exactly matches the supplied schema.
""";

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
                // Invoice extraction is a focused visual data task. Lower thinking
                // effort reduces latency without changing the required output schema.
                thinkingConfig = new
                {
                    thinkingLevel = "low"
                }
            }
        };

        var requestJson = JsonSerializer.Serialize(payload);
        var modelCandidates = new[] { primaryModel, fallbackModel }
            .Where(candidate => !string.IsNullOrWhiteSpace(candidate))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        const int timeoutSeconds = 90;
        const int primaryMaxAttempts = 3; // Initial request plus two retries for temporary 503 overloads.
        HttpResponseMessage? response = null;
        var responseBody = string.Empty;
        var selectedModel = primaryModel;
        var fallbackUsed = false;
        var apiStopwatch = Stopwatch.StartNew();

        for (var modelIndex = 0; modelIndex < modelCandidates.Length; modelIndex++)
        {
            var currentModel = modelCandidates[modelIndex];
            var endpoint = string.Format(
                CultureInfo.InvariantCulture,
                EndpointTemplate,
                Uri.EscapeDataString(currentModel));
            var maxAttempts = modelIndex == 0 ? primaryMaxAttempts : 1;
            var switchToFallback = false;

            for (var attempt = 1; attempt <= maxAttempts; attempt++)
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
                request.Headers.Add("x-goog-api-key", apiKey);
                request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                request.Content = new StringContent(
                    requestJson,
                    Encoding.UTF8,
                    "application/json");

                using var requestTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                requestTimeout.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));

                var attemptStopwatch = Stopwatch.StartNew();
                HttpResponseMessage attemptResponse;
                string attemptResponseBody;

                try
                {
                    attemptResponse = await _httpClient.SendAsync(
                        request,
                        HttpCompletionOption.ResponseHeadersRead,
                        requestTimeout.Token);

                    try
                    {
                        attemptResponseBody = await attemptResponse.Content.ReadAsStringAsync(requestTimeout.Token);
                    }
                    catch
                    {
                        attemptResponse.Dispose();
                        throw;
                    }
                }
                catch (OperationCanceledException ex)
                    when (!cancellationToken.IsCancellationRequested && requestTimeout.IsCancellationRequested)
                {
                    attemptStopwatch.Stop();

                    _logger.LogWarning(
                        ex,
                        "Gemini invoice request timed out. Model {Model}; attempt {AttemptNumber}; elapsed {ElapsedMilliseconds} ms; input {InputBytes} bytes.",
                        currentModel,
                        attempt,
                        attemptStopwatch.ElapsedMilliseconds,
                        memory.Length);

                    if (modelIndex + 1 < modelCandidates.Length)
                    {
                        _logger.LogWarning(
                            "Switching invoice analysis from {PrimaryModel} to fallback model {FallbackModel} after a timeout.",
                            currentModel,
                            modelCandidates[modelIndex + 1]);
                        switchToFallback = true;
                        break;
                    }

                    throw new TimeoutException(
                        $"Gemini did not respond within {timeoutSeconds} seconds. Please retry. If it happens again, use a clear, well-lit invoice image.",
                        ex);
                }

                attemptStopwatch.Stop();

                if (attemptResponse.StatusCode == System.Net.HttpStatusCode.ServiceUnavailable &&
                    attempt < maxAttempts)
                {
                    var retryDelay = GetRetryDelay(attemptResponse, attempt);

                    _logger.LogWarning(
                        "Gemini returned HTTP {StatusCode} for invoice analysis using model {Model}. Retrying attempt {NextAttempt} of {MaxAttempts} after {DelayMilliseconds} ms. Request time: {ElapsedMilliseconds} ms.",
                        (int)attemptResponse.StatusCode,
                        currentModel,
                        attempt + 1,
                        maxAttempts,
                        (int)retryDelay.TotalMilliseconds,
                        attemptStopwatch.ElapsedMilliseconds);

                    attemptResponse.Dispose();
                    await Task.Delay(retryDelay, cancellationToken);
                    continue;
                }

                var canUseFallback =
                    modelIndex + 1 < modelCandidates.Length &&
                    (attemptResponse.StatusCode == System.Net.HttpStatusCode.ServiceUnavailable ||
                     attemptResponse.StatusCode == System.Net.HttpStatusCode.TooManyRequests ||
                     (int)attemptResponse.StatusCode >= 500);

                if (canUseFallback)
                {
                    _logger.LogWarning(
                        "Gemini primary model {PrimaryModel} returned HTTP {StatusCode} after {ElapsedMilliseconds} ms. Switching to fallback model {FallbackModel}.",
                        currentModel,
                        (int)attemptResponse.StatusCode,
                        attemptStopwatch.ElapsedMilliseconds,
                        modelCandidates[modelIndex + 1]);

                    attemptResponse.Dispose();
                    switchToFallback = true;
                    break;
                }

                response = attemptResponse;
                responseBody = attemptResponseBody;
                selectedModel = currentModel;
                fallbackUsed = modelIndex > 0;
                break;
            }

            if (response is not null)
                break;

            if (!switchToFallback)
                break;
        }

        apiStopwatch.Stop();

        if (response is null)
            throw new InvalidOperationException("Gemini did not return a response from the primary or fallback model.");

        using var responseToDispose = response;

        if (response.IsSuccessStatusCode)
        {
            _logger.LogInformation(
                "Gemini invoice API response succeeded. Model {Model}; fallback used {FallbackUsed}; HTTP {StatusCode}; elapsed {ElapsedMilliseconds} ms; input {InputBytes} bytes; MIME {MimeType}.",
                selectedModel,
                fallbackUsed,
                (int)response.StatusCode,
                apiStopwatch.ElapsedMilliseconds,
                memory.Length,
                mimeType);
        }
        else
        {
            _logger.LogWarning(
                "Gemini invoice API response failed. Model {Model}; fallback used {FallbackUsed}; HTTP {StatusCode}; elapsed {ElapsedMilliseconds} ms; input {InputBytes} bytes; response: {Response}",
                selectedModel,
                fallbackUsed,
                (int)response.StatusCode,
                apiStopwatch.ElapsedMilliseconds,
                memory.Length,
                responseBody.Length > 2000 ? responseBody[..2000] : responseBody);
        }

        if (!response.IsSuccessStatusCode)
        {

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

        var items = extracted.Items
            .Take(200)
            .Select((item, index) => NormalizeItem(item, index + 1))
            .Where(item =>
                !string.IsNullOrWhiteSpace(item.ProductName) ||
                !string.IsNullOrWhiteSpace(item.BatchNumber) ||
                !string.IsNullOrWhiteSpace(item.ExpiryDateText) ||
                item.Quantity > 0m)
            .ToList();

        _logger.LogInformation(
            "Gemini invoice extraction parsed {ItemCount} item rows using model {Model}. Total API time: {ElapsedMilliseconds} ms; fallback used: {FallbackUsed}.",
            items.Count,
            selectedModel,
            apiStopwatch.ElapsedMilliseconds,
            fallbackUsed);

        return new InvoiceVisionResult(
            Clean(extracted.DistributorName, 200),
            Clean(extracted.InvoiceNumber, 100),
            Clean(extracted.InvoiceDate, 20),
            extracted.TotalAmount > 0m
                ? Math.Min(extracted.TotalAmount, 999_999_999_999m)
                : 0m,
            items);
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
        var purchaseUnitPrice = item.PurchaseUnitPrice > 0m
            ? Math.Min(item.PurchaseUnitPrice, 999_999_999m)
            : 0m;
        var mrp = item.Mrp > 0m ? Math.Min(item.Mrp, 999_999_999m) : 0m;

        return new InvoiceVisionItem(
            rowNumber,
            product,
            batch,
            expiryText,
            quantity,
            purchaseUnitPrice,
            mrp,
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

    private static TimeSpan GetRetryDelay(HttpResponseMessage response, int retryNumber)
    {
        var retryAfter = response.Headers.RetryAfter;

        if (retryAfter?.Delta is TimeSpan delta &&
            delta > TimeSpan.Zero &&
            delta <= TimeSpan.FromSeconds(10))
        {
            return delta;
        }

        if (retryAfter?.Date is DateTimeOffset retryDate)
        {
            var delay = retryDate - DateTimeOffset.UtcNow;
            if (delay > TimeSpan.Zero && delay <= TimeSpan.FromSeconds(10))
                return delay;
        }

        return retryNumber == 1
            ? TimeSpan.FromSeconds(2)
            : TimeSpan.FromSeconds(4);
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
        [JsonPropertyName("distributorName")]
        public string? DistributorName { get; set; }

        [JsonPropertyName("invoiceNumber")]
        public string? InvoiceNumber { get; set; }

        [JsonPropertyName("invoiceDate")]
        public string? InvoiceDate { get; set; }

        [JsonPropertyName("totalAmount")]
        public decimal TotalAmount { get; set; }

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

        [JsonPropertyName("purchaseUnitPrice")]
        public decimal PurchaseUnitPrice { get; set; }

        [JsonPropertyName("mrp")]
        public decimal Mrp { get; set; }

        [JsonPropertyName("confidence")]
        public decimal Confidence { get; set; }
    }
}

public sealed record InvoiceVisionResult(
    string DistributorName,
    string InvoiceNumber,
    string InvoiceDateText,
    decimal TotalAmount,
    IReadOnlyList<InvoiceVisionItem> Items);

public sealed record InvoiceVisionItem(
    int RowNumber,
    string ProductName,
    string BatchNumber,
    string ExpiryDateText,
    decimal Quantity,
    decimal PurchaseUnitPrice,
    decimal Mrp,
    decimal Confidence);
