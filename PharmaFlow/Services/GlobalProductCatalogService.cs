using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using PharmaFlow.Data;
using PharmaFlow.Models;

namespace PharmaFlow.Services;

public interface IGlobalProductCatalogService
{
    Task<IReadOnlyList<GlobalProductSearchResult>> SearchAsync(
        string query,
        CancellationToken cancellationToken);

    Task<GlobalProductSearchResult?> GetAsync(
        string source,
        string externalId,
        long? catalogId,
        CancellationToken cancellationToken);
}

public sealed class GlobalProductSearchResult
{
    public long? CatalogId { get; init; }
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

public sealed class GlobalProductCatalogService : IGlobalProductCatalogService
{
    private const int ProviderLimit = 20;
    private const string IndiaSource = "india-medicine-api";
    private const string OpenFdaSource = "openfda-ndc";
    private const string OpenFoodFactsSource = "openfoodfacts";

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

    public async Task<IReadOnlyList<GlobalProductSearchResult>> SearchAsync(
        string query,
        CancellationToken cancellationToken)
    {
        var normalizedQuery = NormalizeQuery(query);
        if (normalizedQuery.Length < 2)
            return [];

        var indiaTask = SearchIndiaMedicinesAsync(normalizedQuery, cancellationToken);
        var openFdaTask = SearchOpenFdaAsync(normalizedQuery, cancellationToken);
        var openFoodFactsTask = SearchOpenFoodFactsAsync(normalizedQuery, cancellationToken);

        var indiaResults = await indiaTask;
        var openFdaResults = await openFdaTask;
        var openFoodFactsResults = await openFoodFactsTask;

        // Only Indian medicine results are persisted in product_catalog.
        // OpenFDA and Open Food Facts remain live API results.
        if (indiaResults.Count > 0)
            indiaResults = await CacheIndiaResultsAsync(indiaResults, cancellationToken);

        var resultByKey = new Dictionary<string, GlobalProductSearchResult>(
            StringComparer.OrdinalIgnoreCase);

        foreach (var item in indiaResults)
            resultByKey[$"{item.Source}:{item.ExternalId}"] = item;

        foreach (var item in openFdaResults)
            resultByKey[$"{item.Source}:{item.ExternalId}"] = item;

        foreach (var item in openFoodFactsResults)
            resultByKey[$"{item.Source}:{item.ExternalId}"] = item;

        return resultByKey.Values
            .OrderBy(item => ProductTypeSort(item.ProductType))
            .ThenBy(item => item.ProductName)
            .Take(100)
            .ToList();
    }

    public async Task<GlobalProductSearchResult?> GetAsync(
        string source,
        string externalId,
        long? catalogId,
        CancellationToken cancellationToken)
    {
        var normalizedSource = NormalizeSource(source);
        var normalizedExternalId = NormalizeExternalId(externalId);

        if (string.IsNullOrWhiteSpace(normalizedSource) ||
            string.IsNullOrWhiteSpace(normalizedExternalId))
            return null;

        if (normalizedSource == IndiaSource)
        {
            if (!catalogId.HasValue || catalogId.Value <= 0)
                return null;

            var catalog = await _dbContext.ProductCatalog
                .AsNoTracking()
                .SingleOrDefaultAsync(
                    item => item.CatalogId == catalogId.Value &&
                            item.Source == IndiaSource &&
                            item.ExternalId == normalizedExternalId,
                    cancellationToken);

            return catalog is null ? null : MapCatalog(catalog);
        }

        return normalizedSource switch
        {
            OpenFdaSource => await GetOpenFdaAsync(normalizedExternalId, cancellationToken),
            OpenFoodFactsSource => await GetOpenFoodFactsAsync(normalizedExternalId, cancellationToken),
            _ => null
        };
    }

    private async Task<IReadOnlyList<GlobalProductSearchResult>> CacheIndiaResultsAsync(
        IReadOnlyList<GlobalProductSearchResult> results,
        CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        var cacheDays = Math.Clamp(
            _configuration.GetValue<int?>("GlobalProductCatalog:IndiaCacheDays") ?? 30,
            1,
            365);

        var externalIds = results
            .Select(item => item.ExternalId)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var existingRows = await _dbContext.ProductCatalog
            .Where(item =>
                item.Source == IndiaSource &&
                externalIds.Contains(item.ExternalId))
            .ToListAsync(cancellationToken);

        foreach (var result in results)
        {
            var existing = existingRows.FirstOrDefault(
                item => string.Equals(
                    item.ExternalId,
                    result.ExternalId,
                    StringComparison.OrdinalIgnoreCase));

            if (existing is null)
            {
                _dbContext.ProductCatalog.Add(new ProductCatalog
                {
                    Source = IndiaSource,
                    ExternalId = result.ExternalId,
                    ProductType = result.ProductType,
                    ProductName = result.ProductName,
                    GenericName = result.GenericName,
                    BrandName = result.BrandName,
                    Manufacturer = result.Manufacturer,
                    DosageForm = result.DosageForm,
                    Strength = result.Strength,
                    PackSize = result.PackSize,
                    Barcode = result.Barcode,
                    HsnCode = result.HsnCode,
                    GstRate = result.GstRate,
                    IsPrescriptionRequired = result.IsPrescriptionRequired,
                    SourceUrl = result.SourceUrl,
                    FirstSeenAt = now,
                    LastSyncedAt = now,
                    CacheExpiresAt = now.AddDays(cacheDays),
                    UpdatedAt = now
                });

                continue;
            }

            ApplyResult(existing, result, now, cacheDays);
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        // Remove expired, unreferenced search-cache rows. Selected products remain
        // because their products.catalog_id still references the catalog row.
        var expiredRows = await _dbContext.ProductCatalog
            .Where(item =>
                item.Source == IndiaSource &&
                item.CacheExpiresAt < now &&
                !_dbContext.Products.Any(product => product.CatalogId == item.CatalogId))
            .OrderBy(item => item.CacheExpiresAt)
            .Take(200)
            .ToListAsync(cancellationToken);

        if (expiredRows.Count > 0)
        {
            _dbContext.ProductCatalog.RemoveRange(expiredRows);
            await _dbContext.SaveChangesAsync(cancellationToken);
        }

        var cachedRows = await _dbContext.ProductCatalog
            .AsNoTracking()
            .Where(item =>
                item.Source == IndiaSource &&
                externalIds.Contains(item.ExternalId))
            .Select(item => new { item.CatalogId, item.ExternalId })
            .ToListAsync(cancellationToken);

        var catalogIdByExternalId = cachedRows
            .ToDictionary(item => item.ExternalId, item => item.CatalogId, StringComparer.OrdinalIgnoreCase);

        return results
            .Where(item => catalogIdByExternalId.ContainsKey(item.ExternalId))
            .Select(item => new GlobalProductSearchResult
            {
                CatalogId = catalogIdByExternalId[item.ExternalId],
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
                SourceUrl = item.SourceUrl
            })
            .ToList();
    }

    private async Task<IReadOnlyList<GlobalProductSearchResult>> SearchIndiaMedicinesAsync(
        string query,
        CancellationToken cancellationToken)
    {
        var baseUrl = _configuration["GlobalProductCatalog:IndiaMedicineApiBaseUrl"];
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            _logger.LogWarning("India Medicine API base URL is not configured.");
            return [];
        }

        try
        {
            var client = _httpClientFactory.CreateClient("IndiaMedicine");
            var url =
                $"{baseUrl.TrimEnd('/')}/search?q={Uri.EscapeDataString(query)}&limit={ProviderLimit}";

            using var response = await client.GetAsync(url, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "India Medicine API returned HTTP {StatusCode}.",
                    response.StatusCode);
                return [];
            }

            var payload = await response.Content.ReadFromJsonAsync<IndiaMedicineSearchResponse>(
                cancellationToken: cancellationToken);

            return payload?.Results?
                .Where(item => item.Id > 0 && !string.IsNullOrWhiteSpace(item.ProductName))
                .Select(item => new GlobalProductSearchResult
                {
                    Source = IndiaSource,
                    ExternalId = item.Id.ToString(),
                    ProductType = "medicine",
                    ProductName = item.ProductName!.Trim(),
                    GenericName = item.SaltComposition?.Trim(),
                    DosageForm = item.DosageForm?.Trim(),
                    IsPrescriptionRequired = item.IsPrescriptionRequired
                })
                .Take(ProviderLimit)
                .ToList()
                ?? [];
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "India Medicine API search failed.");
            return [];
        }
    }

    private async Task<GlobalProductSearchResult?> GetIndiaMedicineAsync(
        string externalId,
        CancellationToken cancellationToken)
    {
        var baseUrl = _configuration["GlobalProductCatalog:IndiaMedicineApiBaseUrl"];
        if (string.IsNullOrWhiteSpace(baseUrl) ||
            !int.TryParse(externalId, out var medicineId) ||
            medicineId <= 0)
            return null;

        try
        {
            var client = _httpClientFactory.CreateClient("IndiaMedicine");
            var url = $"{baseUrl.TrimEnd('/')}/medicine/{medicineId}";

            using var response = await client.GetAsync(url, cancellationToken);
            if (!response.IsSuccessStatusCode)
                return null;

            var item = await response.Content.ReadFromJsonAsync<IndiaMedicineResult>(
                cancellationToken: cancellationToken);

            return item is null || item.Id <= 0 || string.IsNullOrWhiteSpace(item.ProductName)
                ? null
                : new GlobalProductSearchResult
                {
                    Source = IndiaSource,
                    ExternalId = item.Id.ToString(),
                    ProductType = "medicine",
                    ProductName = item.ProductName!.Trim(),
                    GenericName = item.SaltComposition?.Trim(),
                    DosageForm = item.DosageForm?.Trim(),
                    IsPrescriptionRequired = item.IsPrescriptionRequired
                };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "India Medicine API detail lookup failed.");
            return null;
        }
    }

    private async Task<IReadOnlyList<GlobalProductSearchResult>> SearchOpenFdaAsync(
        string query,
        CancellationToken cancellationToken)
    {
        if (!(_configuration.GetValue<bool?>("GlobalProductCatalog:OpenFdaEnabled") ?? true))
            return [];

        try
        {
            var client = _httpClientFactory.CreateClient("OpenFda");
            var safeQuery = EscapeOpenFdaValue(query);
            var searchExpression =
                $"brand_name:\"{safeQuery}\" OR generic_name:\"{safeQuery}\"";

            var apiKey = _configuration["GlobalProductCatalog:OpenFdaApiKey"];
            var keyPart = string.IsNullOrWhiteSpace(apiKey)
                ? string.Empty
                : $"&api_key={Uri.EscapeDataString(apiKey.Trim())}";

            var url =
                $"/drug/ndc.json?search={Uri.EscapeDataString(searchExpression)}" +
                $"&limit={ProviderLimit}{keyPart}";

            using var response = await client.GetAsync(url, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "openFDA returned HTTP {StatusCode}.",
                    response.StatusCode);
                return [];
            }

            var payload = await response.Content.ReadFromJsonAsync<OpenFdaNdcSearchResponse>(
                cancellationToken: cancellationToken);

            return payload?.Results?
                .Select(MapOpenFda)
                .Where(item => item is not null)
                .Select(item => item!)
                .Take(ProviderLimit)
                .ToList()
                ?? [];
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "openFDA search failed.");
            return [];
        }
    }

    private async Task<GlobalProductSearchResult?> GetOpenFdaAsync(
        string externalId,
        CancellationToken cancellationToken)
    {
        try
        {
            var client = _httpClientFactory.CreateClient("OpenFda");
            var safeId = EscapeOpenFdaValue(externalId);
            var searchExpression =
                $"package_ndc:\"{safeId}\" OR product_ndc:\"{safeId}\"";

            var apiKey = _configuration["GlobalProductCatalog:OpenFdaApiKey"];
            var keyPart = string.IsNullOrWhiteSpace(apiKey)
                ? string.Empty
                : $"&api_key={Uri.EscapeDataString(apiKey.Trim())}";

            var url =
                $"/drug/ndc.json?search={Uri.EscapeDataString(searchExpression)}" +
                $"&limit=1{keyPart}";

            using var response = await client.GetAsync(url, cancellationToken);
            if (!response.IsSuccessStatusCode)
                return null;

            var payload = await response.Content.ReadFromJsonAsync<OpenFdaNdcSearchResponse>(
                cancellationToken: cancellationToken);

            return payload?.Results?.Select(MapOpenFda).FirstOrDefault();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "openFDA detail lookup failed.");
            return null;
        }
    }

    private async Task<IReadOnlyList<GlobalProductSearchResult>> SearchOpenFoodFactsAsync(
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

            using var response = await client.GetAsync(url, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "Open Food Facts returned HTTP {StatusCode}.",
                    response.StatusCode);
                return [];
            }

            var payload = await response.Content.ReadFromJsonAsync<OpenFoodFactsSearchResponse>(
                cancellationToken: cancellationToken);

            return payload?.Products?
                .Select(MapOpenFoodFacts)
                .Where(item => item is not null)
                .Select(item => item!)
                .Where(item => !string.IsNullOrWhiteSpace(item.ExternalId))
                .Take(ProviderLimit)
                .ToList()
                ?? [];
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Open Food Facts search failed.");
            return [];
        }
    }

    private async Task<GlobalProductSearchResult?> GetOpenFoodFactsAsync(
        string barcode,
        CancellationToken cancellationToken)
    {
        try
        {
            var client = _httpClientFactory.CreateClient("OpenFoodFacts");
            var url =
                $"/api/v2/product/{Uri.EscapeDataString(barcode)}.json" +
                "?fields=code,product_name,brands,categories,quantity,manufacturing_places,url";

            using var response = await client.GetAsync(url, cancellationToken);
            if (!response.IsSuccessStatusCode)
                return null;

            var payload = await response.Content.ReadFromJsonAsync<OpenFoodFactsProductResponse>(
                cancellationToken: cancellationToken);

            return payload?.Product is null
                ? null
                : MapOpenFoodFacts(payload.Product);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Open Food Facts detail lookup failed.");
            return null;
        }
    }

    private static GlobalProductSearchResult? MapOpenFda(OpenFdaNdcResult item)
    {
        var externalId = FirstNonEmpty(item.PackageNdc, item.ProductNdc);
        var productName = FirstNonEmpty(item.BrandName, item.GenericName);

        if (string.IsNullOrWhiteSpace(externalId) ||
            string.IsNullOrWhiteSpace(productName))
            return null;

        return new GlobalProductSearchResult
        {
            Source = OpenFdaSource,
            ExternalId = externalId,
            ProductType = "medicine",
            ProductName = productName,
            GenericName = item.GenericName?.Trim(),
            BrandName = item.BrandName?.Trim(),
            Manufacturer = item.ManufacturerName?.Trim(),
            DosageForm = item.DosageForm?.Trim(),
            Barcode = FirstOpenFdaValue(item.OpenFda?.Upc),
            IsPrescriptionRequired = IsPrescription(item.ProductType)
        };
    }

    private static GlobalProductSearchResult? MapOpenFoodFacts(OpenFoodFactsProduct item)
    {
        var externalId = item.Code?.Trim();
        var productName = item.ProductName?.Trim();

        if (string.IsNullOrWhiteSpace(externalId) ||
            string.IsNullOrWhiteSpace(productName))
            return null;

        return new GlobalProductSearchResult
        {
            Source = OpenFoodFactsSource,
            ExternalId = externalId,
            ProductType = NormalizeProductType(item.Categories),
            ProductName = productName,
            BrandName = item.Brands?.Trim(),
            Manufacturer = item.ManufacturingPlaces?.Trim(),
            PackSize = item.Quantity?.Trim(),
            Barcode = externalId,
            SourceUrl = item.Url?.Trim()
        };
    }

    private static GlobalProductSearchResult MapCatalog(ProductCatalog item) =>
        new()
        {
            CatalogId = item.CatalogId,
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
            SourceUrl = item.SourceUrl
        };

    private static void ApplyResult(
        ProductCatalog target,
        GlobalProductSearchResult source,
        DateTime now,
        int cacheDays)
    {
        target.ProductType = source.ProductType;
        target.ProductName = source.ProductName;
        target.GenericName = source.GenericName;
        target.BrandName = source.BrandName;
        target.Manufacturer = source.Manufacturer;
        target.DosageForm = source.DosageForm;
        target.Strength = source.Strength;
        target.PackSize = source.PackSize;
        target.Barcode = source.Barcode;
        target.HsnCode = source.HsnCode;
        target.GstRate = source.GstRate;
        target.IsPrescriptionRequired = source.IsPrescriptionRequired;
        target.SourceUrl = source.SourceUrl;
        target.LastSyncedAt = now;
        target.CacheExpiresAt = now.AddDays(cacheDays);
        target.UpdatedAt = now;
    }

    private static int ProductTypeSort(string? productType) =>
        productType switch
        {
            "medicine" => 0,
            "cosmetic" => 1,
            _ => 2
        };

    private static string NormalizeSource(string? source) =>
        source?.Trim().ToLowerInvariant() switch
        {
            IndiaSource => IndiaSource,
            OpenFdaSource => OpenFdaSource,
            OpenFoodFactsSource => OpenFoodFactsSource,
            _ => string.Empty
        };

    private static string NormalizeExternalId(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : value.Trim()[..Math.Min(value.Trim().Length, 160)];

    private static string NormalizeQuery(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
            return string.Empty;

        var trimmed = query.Trim();
        return trimmed.Length <= 100 ? trimmed : trimmed[..100];
    }

    private static string EscapeOpenFdaValue(string value) =>
        value.Replace("\", "\\").Replace(""", "\"");

    private static string NormalizeProductType(string? categories) =>
        string.IsNullOrWhiteSpace(categories)
            ? "product"
            : categories.Contains("beauty", StringComparison.OrdinalIgnoreCase)
                ? "cosmetic"
                : "product";

    private static bool IsPrescription(string? productType) =>
        !string.IsNullOrWhiteSpace(productType) &&
        productType.Contains("prescription", StringComparison.OrdinalIgnoreCase);

    private static string? FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim();

    private static string? FirstOpenFdaValue(string[]? values) =>
        values?.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim();

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

    private sealed class OpenFoodFactsProductResponse
    {
        [JsonPropertyName("product")]
        public OpenFoodFactsProduct? Product { get; init; }
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
