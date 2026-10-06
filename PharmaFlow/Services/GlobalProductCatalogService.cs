using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using PharmaFlow.Data;
using PharmaFlow.Models;

namespace PharmaFlow.Services;

public interface IGlobalProductCatalogService
{
    Task<IReadOnlyList<ProductCatalog>> SearchAsync(string query, CancellationToken cancellationToken);
}

public sealed class GlobalProductCatalogService : IGlobalProductCatalogService
{
    private const int ProviderLimit = 20;
    private readonly ApplicationDbContext _dbContext;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _configuration;
    private readonly ILogger<GlobalProductCatalogService> _logger;

    public GlobalProductCatalogService(
        ApplicationDbContext dbContext,
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration,
        ILogger<GlobalProductCatalogService> logger)
    {
        _dbContext = dbContext;
        _httpClientFactory = httpClientFactory;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task<IReadOnlyList<ProductCatalog>> SearchAsync(
        string query,
        CancellationToken cancellationToken)
    {
        var normalizedQuery = NormalizeQuery(query);
        if (normalizedQuery.Length < 2)
            return [];

        var providerTasks = new[]
        {
            SearchIndiaMedicinesAsync(normalizedQuery, cancellationToken),
            SearchOpenFdaAsync(normalizedQuery, cancellationToken),
            SearchOpenFoodFactsAsync(normalizedQuery, cancellationToken)
        };

        var providerResults = await Task.WhenAll(providerTasks);

        var externalResults = providerResults
            .SelectMany(static items => items)
            .Where(static item => !string.IsNullOrWhiteSpace(item.ExternalId) &&
                                  !string.IsNullOrWhiteSpace(item.ProductName))
            .GroupBy(static item => $"{item.Source}:{item.ExternalId}", StringComparer.OrdinalIgnoreCase)
            .Select(static group => group.First())
            .Take(60)
            .ToList();

        await UpsertCatalogAsync(externalResults, cancellationToken);

        var pattern = $"%{EscapeLikePattern(normalizedQuery)}%";

        return await _dbContext.ProductCatalog
            .AsNoTracking()
            .Where(item =>
                EF.Functions.ILike(item.ProductName, pattern) ||
                (item.GenericName != null && EF.Functions.ILike(item.GenericName, pattern)) ||
                (item.BrandName != null && EF.Functions.ILike(item.BrandName, pattern)) ||
                (item.Manufacturer != null && EF.Functions.ILike(item.Manufacturer, pattern)) ||
                (item.Barcode != null && EF.Functions.ILike(item.Barcode, pattern)))
            .OrderBy(item => item.ProductName)
            .ThenBy(item => item.Source)
            .Take(100)
            .ToListAsync(cancellationToken);
    }

    private async Task UpsertCatalogAsync(
        IReadOnlyList<CatalogCandidate> candidates,
        CancellationToken cancellationToken)
    {
        if (candidates.Count == 0)
            return;

        foreach (var item in candidates)
        {
            var existing = await _dbContext.ProductCatalog
                .SingleOrDefaultAsync(
                    catalog => catalog.Source == item.Source &&
                               catalog.ExternalId == item.ExternalId,
                    cancellationToken);

            if (existing is null)
            {
                _dbContext.ProductCatalog.Add(MapCandidate(item));
                continue;
            }

            ApplyCandidate(existing, item);
        }

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex)
        {
            _logger.LogWarning(ex, "Global catalog cache update encountered a database conflict.");
            _dbContext.ChangeTracker.Clear();
        }
    }

    private static void ApplyCandidate(ProductCatalog target, CatalogCandidate item)
    {
        target.ProductType = item.ProductType;
        target.ProductName = item.ProductName;
        target.GenericName = item.GenericName;
        target.BrandName = item.BrandName;
        target.Manufacturer = item.Manufacturer;
        target.DosageForm = item.DosageForm;
        target.Strength = item.Strength;
        target.PackSize = item.PackSize;
        target.Barcode = item.Barcode;
        target.HsnCode = item.HsnCode;
        target.GstRate = item.GstRate;
        target.IsPrescriptionRequired = item.IsPrescriptionRequired;
        target.SourceUrl = item.SourceUrl;
        target.LastSyncedAt = DateTime.UtcNow;
        target.UpdatedAt = DateTime.UtcNow;
    }

    private static ProductCatalog MapCandidate(CatalogCandidate item)
    {
        var now = DateTime.UtcNow;

        return new ProductCatalog
        {
            Source = item.Source,
            ExternalId = item.ExternalId,
            ProductType = item.ProductType,
            ProductName = item.ProductName,
            GenericName = item.GenericName,
            BrandName = item.BrandName,
            Manufacturer = item.Manufacturer,
            DosageForm = item.DosageForm,
            Strength = item.Strength,
            PackSize = item.PackSize,
            Barcode = item.Barcode,
            HsnCode = item.HsnCode,
            GstRate = item.GstRate,
            IsPrescriptionRequired = item.IsPrescriptionRequired,
            SourceUrl = item.SourceUrl,
            FirstSeenAt = now,
            LastSyncedAt = now,
            UpdatedAt = now
        };
    }

    private async Task<IReadOnlyList<CatalogCandidate>> SearchIndiaMedicinesAsync(
        string query,
        CancellationToken cancellationToken)
    {
        var baseUrl = _configuration["GlobalProductCatalog:IndiaMedicineApiBaseUrl"];
        if (string.IsNullOrWhiteSpace(baseUrl))
            return [];

        try
        {
            var client = _httpClientFactory.CreateClient("IndiaMedicine");
            var url = $"{baseUrl.TrimEnd('/')}/search?q={Uri.EscapeDataString(query)}&limit={ProviderLimit}";

            var response = await client.GetAsync(url, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "India medicine provider returned HTTP {StatusCode}.",
                    response.StatusCode);
                return [];
            }

            var payload = await response.Content.ReadFromJsonAsync<IndiaMedicineSearchResponse>(
                cancellationToken: cancellationToken);

            return payload?.Results?
                .Select(item => new CatalogCandidate
                {
                    Source = "india-medicine-api",
                    ExternalId = item.Id.ToString(),
                    ProductType = "medicine",
                    ProductName = item.ProductName?.Trim() ?? string.Empty,
                    GenericName = item.SaltComposition?.Trim(),
                    DosageForm = item.DosageForm?.Trim(),
                    IsPrescriptionRequired = item.IsPrescriptionRequired
                })
                .Where(item => !string.IsNullOrWhiteSpace(item.ProductName))
                .ToList()
                ?? [];
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "India medicine provider search failed.");
            return [];
        }
    }

    private async Task<IReadOnlyList<CatalogCandidate>> SearchOpenFdaAsync(
        string query,
        CancellationToken cancellationToken)
    {
        if (!(_configuration.GetValue<bool?>("GlobalProductCatalog:OpenFdaEnabled") ?? true))
            return [];

        try
        {
            var client = _httpClientFactory.CreateClient("OpenFda");
            var safeQuery = EscapeOpenFdaValue(query);
            var searchExpression = $"brand_name:\"{safeQuery}\" OR generic_name:\"{safeQuery}\"";
            var url =
                $"/drug/ndc.json?search={Uri.EscapeDataString(searchExpression)}&limit={ProviderLimit}";

            var response = await client.GetAsync(url, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "openFDA provider returned HTTP {StatusCode}.",
                    response.StatusCode);
                return [];
            }

            var payload = await response.Content.ReadFromJsonAsync<OpenFdaNdcSearchResponse>(
                cancellationToken: cancellationToken);

            return payload?.Results?
                .Select(item => new CatalogCandidate
                {
                    Source = "openfda-ndc",
                    ExternalId = FirstNonEmpty(item.PackageNdc, item.ProductNdc, item.ApplicationNumber)
                                 ?? Guid.NewGuid().ToString("N"),
                    ProductType = "medicine",
                    ProductName = FirstNonEmpty(item.BrandName, item.GenericName) ?? string.Empty,
                    GenericName = item.GenericName,
                    BrandName = item.BrandName,
                    Manufacturer = item.ManufacturerName,
                    DosageForm = item.DosageForm,
                    Barcode = FirstOpenFdaValue(item.OpenFda?.Upc),
                    IsPrescriptionRequired = IsPrescription(item.ProductType)
                })
                .Where(item => !string.IsNullOrWhiteSpace(item.ProductName))
                .ToList()
                ?? [];
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "openFDA provider search failed.");
            return [];
        }
    }

    private async Task<IReadOnlyList<CatalogCandidate>> SearchOpenFoodFactsAsync(
        string query,
        CancellationToken cancellationToken)
    {
        if (!(_configuration.GetValue<bool?>("GlobalProductCatalog:OpenFoodFactsEnabled") ?? true) ||
            query.Length < 3)
            return [];

        try
        {
            var client = _httpClientFactory.CreateClient("OpenFoodFacts");
            var url =
                $"/cgi/search.pl?search_terms={Uri.EscapeDataString(query)}" +
                $"&search_simple=1&action=process&json=1&page_size={ProviderLimit}" +
                "&product_type=all" +
                "&fields=code,product_name,brands,categories,quantity,manufacturing_places,url";

            var response = await client.GetAsync(url, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "Open Food Facts provider returned HTTP {StatusCode}.",
                    response.StatusCode);
                return [];
            }

            var payload = await response.Content.ReadFromJsonAsync<OpenFoodFactsSearchResponse>(
                cancellationToken: cancellationToken);

            return payload?.Products?
                .Select(item => new CatalogCandidate
                {
                    Source = "openfoodfacts",
                    ExternalId = item.Code?.Trim() ?? string.Empty,
                    ProductType = NormalizeProductType(item.Categories),
                    ProductName = item.ProductName?.Trim() ?? string.Empty,
                    BrandName = item.Brands?.Trim(),
                    Manufacturer = item.ManufacturingPlaces?.Trim(),
                    PackSize = item.Quantity?.Trim(),
                    Barcode = item.Code?.Trim(),
                    SourceUrl = item.Url?.Trim()
                })
                .Where(item => !string.IsNullOrWhiteSpace(item.ProductName) &&
                               !string.IsNullOrWhiteSpace(item.ExternalId))
                .ToList()
                ?? [];
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Open Food Facts provider search failed.");
            return [];
        }
    }

    private static string NormalizeQuery(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
            return string.Empty;

        var trimmed = query.Trim();
        return trimmed.Length <= 100 ? trimmed : trimmed[..100];
    }

    private static string EscapeLikePattern(string value) =>
        value.Replace("\", "\\").Replace("%", "\%").Replace("_", "\_");

    private static string EscapeOpenFdaValue(string value) =>
        value.Replace("\", "\\").Replace(""", "\"");

    private static string NormalizeProductType(string? categories) =>
        string.IsNullOrWhiteSpace(categories)
            ? "product"
            : categories.Contains("beauty", StringComparison.OrdinalIgnoreCase)
                ? "cosmetic"
                : categories.Contains("pet", StringComparison.OrdinalIgnoreCase)
                    ? "pet-product"
                    : "product";

    private static bool IsPrescription(string? productType) =>
        !string.IsNullOrWhiteSpace(productType) &&
        productType.Contains("prescription", StringComparison.OrdinalIgnoreCase);

    private static string? FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim();

    private static string? FirstOpenFdaValue(string[]? values) =>
        values?.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim();

    private sealed class CatalogCandidate
    {
        public string Source { get; init; } = string.Empty;
        public string ExternalId { get; init; } = string.Empty;
        public string ProductType { get; init; } = string.Empty;
        public string ProductName { get; init; } = string.Empty;
        public string? GenericName { get; init; }
        public string? BrandName { get; init; }
        public string? Manufacturer { get; init; }
        public string? DosageForm { get; init; }
        public string? Strength { get; init; }
        public string? PackSize { get; init; }
        public string? Barcode { get; init; }
        public string? HsnCode { get; init; }
        public decimal? GstRate { get; init; }
        public bool IsPrescriptionRequired { get; init; }
        public string? SourceUrl { get; init; }
    }

    private sealed class IndiaMedicineSearchResponse
    {
        [JsonPropertyName("results")]
        public List<IndiaMedicineResult>? Results { get; init; }
    }

    private sealed class IndiaMedicineResult
    {
        [JsonPropertyName("id")]
        public int Id { get; init; }

        [JsonPropertyName("product_name")]
        public string? ProductName { get; init; }

        [JsonPropertyName("salt_composition")]
        public string? SaltComposition { get; init; }

        [JsonPropertyName("dosage_form")]
        public string? DosageForm { get; init; }

        [JsonPropertyName("is_prescription_required")]
        public bool IsPrescriptionRequired { get; init; }
    }

    private sealed class OpenFdaNdcSearchResponse
    {
        [JsonPropertyName("results")]
        public List<OpenFdaNdcResult>? Results { get; init; }
    }

    private sealed class OpenFdaNdcResult
    {
        [JsonPropertyName("product_ndc")]
        public string? ProductNdc { get; init; }

        [JsonPropertyName("package_ndc")]
        public string? PackageNdc { get; init; }

        [JsonPropertyName("application_number")]
        public string? ApplicationNumber { get; init; }

        [JsonPropertyName("brand_name")]
        public string? BrandName { get; init; }

        [JsonPropertyName("generic_name")]
        public string? GenericName { get; init; }

        [JsonPropertyName("manufacturer_name")]
        public string? ManufacturerName { get; init; }

        [JsonPropertyName("dosage_form")]
        public string? DosageForm { get; init; }

        [JsonPropertyName("product_type")]
        public string? ProductType { get; init; }

        [JsonPropertyName("openfda")]
        public OpenFdaIdentifiers? OpenFda { get; init; }
    }

    private sealed class OpenFdaIdentifiers
    {
        [JsonPropertyName("upc")]
        public string[]? Upc { get; init; }
    }

    private sealed class OpenFoodFactsSearchResponse
    {
        [JsonPropertyName("products")]
        public List<OpenFoodFactsProduct>? Products { get; init; }
    }

    private sealed class OpenFoodFactsProduct
    {
        [JsonPropertyName("code")]
        public string? Code { get; init; }

        [JsonPropertyName("product_name")]
        public string? ProductName { get; init; }

        [JsonPropertyName("brands")]
        public string? Brands { get; init; }

        [JsonPropertyName("categories")]
        public string? Categories { get; init; }

        [JsonPropertyName("quantity")]
        public string? Quantity { get; init; }

        [JsonPropertyName("manufacturing_places")]
        public string? ManufacturingPlaces { get; init; }

        [JsonPropertyName("url")]
        public string? Url { get; init; }
    }
}
