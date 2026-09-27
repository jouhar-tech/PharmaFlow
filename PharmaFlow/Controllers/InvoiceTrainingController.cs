using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using PharmaFlow.Services;

namespace PharmaFlow.Controllers;

[ApiController]
[Route("InvoiceTraining")]
public sealed class InvoiceTrainingController : ControllerBase
{
    private const int MaxFileBytes = 25 * 1024 * 1024;
    private const int MaxItems = 200;

    private readonly IInvoiceVisionService _invoiceVisionService;
    private readonly IConfiguration _configuration;
    private readonly ILogger<InvoiceTrainingController> _logger;

    public InvoiceTrainingController(
        IInvoiceVisionService invoiceVisionService,
        IConfiguration configuration,
        ILogger<InvoiceTrainingController> logger)
    {
        _invoiceVisionService = invoiceVisionService;
        _configuration = configuration;
        _logger = logger;
    }

    [HttpPost("DraftLabel")]
    [IgnoreAntiforgeryToken]
    [RequestSizeLimit(26_214_400)]
    public async Task<IActionResult> DraftLabel(
        [FromForm] IFormFile? invoice,
        [FromForm] string? originalFileName,
        [FromForm] string? sourceType,
        CancellationToken cancellationToken)
    {
        if (!IsAuthorized())
            return Unauthorized(new { message = "Training endpoint authentication failed." });

        if (invoice is null || invoice.Length <= 0)
            return BadRequest(new { message = "Invoice file is required." });

        if (invoice.Length > MaxFileBytes)
            return BadRequest(new { message = "Invoice file exceeds the 25 MB limit." });

        var mimeType = NormalizeMimeType(invoice.ContentType, invoice.FileName);
        if (mimeType is null)
        {
            return BadRequest(new
            {
                message = "Unsupported invoice file type. Use JPEG, PNG, WebP, or PDF."
            });
        }

        var safeFileName = Path.GetFileName(
            string.IsNullOrWhiteSpace(originalFileName)
                ? invoice.FileName
                : originalFileName.Trim());

        if (string.IsNullOrWhiteSpace(safeFileName))
            safeFileName = "invoice";

        if (safeFileName.Length > 255)
            safeFileName = safeFileName[..255];

        var normalizedSourceType = NormalizeSourceType(sourceType);

        try
        {
            await using var stream = invoice.OpenReadStream();

            var extracted = await _invoiceVisionService.ExtractAsync(
                stream,
                mimeType,
                cancellationToken);

            var items = extracted
                .Take(MaxItems)
                .Select(item => new
                {
                    rowNumber = item.RowNumber,
                    productName = string.IsNullOrWhiteSpace(item.ProductName)
                        ? null
                        : item.ProductName,
                    batchNumber = string.IsNullOrWhiteSpace(item.BatchNumber)
                        ? null
                        : item.BatchNumber,
                    expiryDate = NormalizeExpiryDate(item.ExpiryDateText),
                    quantity = item.Quantity > 0m
                        ? Math.Min(item.Quantity, 1_000_000m)
                        : (decimal?)null,
                    confidence = Math.Clamp(item.Confidence, 0m, 100m)
                })
                .Where(item =>
                    item.productName is not null ||
                    item.batchNumber is not null ||
                    item.expiryDate is not null ||
                    item.quantity.HasValue)
                .ToList();

            if (items.Count == 0)
            {
                return UnprocessableEntity(new
                {
                    message = "The AI service did not detect any possible stock rows.",
                    fileName = safeFileName,
                    sourceType = normalizedSourceType
                });
            }

            return Ok(new
            {
                draft = true,
                fileName = safeFileName,
                sourceType = normalizedSourceType,
                generatedAtUtc = DateTime.UtcNow,
                items
            });
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(
                ex,
                "Invoice training draft labeling failed for {FileName}.",
                safeFileName);

            return UnprocessableEntity(new { message = ex.Message });
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            return new EmptyResult();
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Unexpected invoice training draft labeling failure for {FileName}.",
                safeFileName);

            return StatusCode(
                StatusCodes.Status500InternalServerError,
                new { message = "The invoice draft could not be generated." });
        }
    }

    private bool IsAuthorized()
    {
        var expectedKey =
            _configuration["InvoiceTraining:ApiKey"] ??
            Environment.GetEnvironmentVariable("PHARMAFLOW_TRAINING_API_KEY");

        if (string.IsNullOrWhiteSpace(expectedKey))
            return false;

        if (!Request.Headers.TryGetValue("X-PharmaFlow-Training-Key", out var provided))
            return false;

        var expectedBytes = Encoding.UTF8.GetBytes(expectedKey);
        var providedBytes = Encoding.UTF8.GetBytes(provided.ToString());

        return expectedBytes.Length == providedBytes.Length &&
               CryptographicOperations.FixedTimeEquals(expectedBytes, providedBytes);
    }

    private static string? NormalizeMimeType(string? contentType, string? fileName)
    {
        var normalized = contentType?.Trim().ToLowerInvariant();

        return normalized switch
        {
            "image/jpeg" => "image/jpeg",
            "image/png" => "image/png",
            "image/webp" => "image/webp",
            "application/pdf" => "application/pdf",
            _ => NormalizeMimeTypeFromExtension(fileName)
        };
    }

    private static string? NormalizeMimeTypeFromExtension(string? fileName)
    {
        var extension = Path.GetExtension(fileName ?? string.Empty).ToLowerInvariant();

        return extension switch
        {
            ".jpg" or ".jpeg" => "image/jpeg",
            ".png" => "image/png",
            ".webp" => "image/webp",
            ".pdf" => "application/pdf",
            _ => null
        };
    }

    private static string NormalizeSourceType(string? sourceType)
    {
        var normalized = sourceType?.Trim().ToLowerInvariant();

        return normalized switch
        {
            "camera" => "camera",
            "scan" => "scan",
            "pdf" => "pdf",
            "upload" => "upload",
            _ => "upload"
        };
    }

    private static string? NormalizeExpiryDate(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return null;

        var value = raw.Trim().Replace(".", "/").Replace("-", "/");

        var formats = new[]
        {
            "yyyy/MM/dd",
            "dd/MM/yyyy",
            "MM/yyyy",
            "M/yyyy",
            "MM/yy",
            "M/yy"
        };

        foreach (var format in formats)
        {
            if (!DateTime.TryParseExact(
                    value,
                    format,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.None,
                    out var parsed))
                continue;

            if (format is "MM/yyyy" or "M/yyyy" or "MM/yy" or "M/yy")
            {
                var month = parsed.Month;
                var year = parsed.Year;

                return new DateTime(
                        year,
                        month,
                        DateTime.DaysInMonth(year, month),
                        0,
                        0,
                        0,
                        DateTimeKind.Utc)
                    .ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            }

            return parsed.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        }

        if (DateTime.TryParse(
                raw.Trim(),
                CultureInfo.InvariantCulture,
                DateTimeStyles.AllowWhiteSpaces,
                out var fallback) &&
            fallback.Year is >= 2000 and <= 2100)
        {
            return fallback.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        }

        return null;
    }
}
