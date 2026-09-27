namespace PharmaFlow.Services;

public sealed class HybridInvoiceVisionService : IInvoiceVisionService
{
    private readonly PaddleOcrVlInvoiceVisionService _paddleOcr;
    private readonly GeminiInvoiceVisionService _gemini;
    private readonly IConfiguration _configuration;
    private readonly ILogger<HybridInvoiceVisionService> _logger;

    public HybridInvoiceVisionService(
        PaddleOcrVlInvoiceVisionService paddleOcr,
        GeminiInvoiceVisionService gemini,
        IConfiguration configuration,
        ILogger<HybridInvoiceVisionService> logger)
    {
        _paddleOcr = paddleOcr;
        _gemini = gemini;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task<IReadOnlyList<InvoiceVisionItem>> ExtractAsync(
        Stream fileStream,
        string mimeType,
        CancellationToken cancellationToken)
    {
        var usePaddleOcr = _configuration.GetValue("PaddleOCR:Enabled", true);

        if (!usePaddleOcr)
        {
            _logger.LogInformation("PaddleOCR-VL is disabled. Using Gemini invoice extraction.");
            return await _gemini.ExtractAsync(fileStream, mimeType, cancellationToken);
        }

        using var memory = new MemoryStream();
        await fileStream.CopyToAsync(memory, cancellationToken);
        var fileBytes = memory.ToArray();

        try
        {
            var paddleItems = await _paddleOcr.ExtractAsync(
                new MemoryStream(fileBytes, writable: false),
                mimeType,
                cancellationToken);

            if (paddleItems.Count > 0)
                return paddleItems;

            throw new InvalidOperationException("PaddleOCR returned no usable invoice rows.");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning(
                "PaddleOCR-VL local service timed out. Falling back to Gemini.");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "PaddleOCR-VL invoice extraction failed. Falling back to Gemini.");
        }

        return await _gemini.ExtractAsync(
            new MemoryStream(fileBytes, writable: false),
            mimeType,
            cancellationToken);
    }
}
