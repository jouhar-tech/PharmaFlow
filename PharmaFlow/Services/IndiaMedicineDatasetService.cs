using System.Globalization;
using System.Text;
using CsvHelper;
using CsvHelper.Configuration;

namespace PharmaFlow.Services;

public sealed record IndiaMedicineDatasetItem(
    long Id,
    string ProductName,
    string? Composition,
    string? Manufacturer,
    string? PackSize,
    string? DosageType);

public interface IIndiaMedicineDatasetService
{
    Task<IReadOnlyList<IndiaMedicineDatasetItem>> SearchAsync(
        string query,
        int limit,
        CancellationToken cancellationToken);
}

/// <summary>
/// Searches the Indian medicine CSV directly from disk. It deliberately streams
/// the 30 MB source file instead of loading all ~254k rows into memory, which is
/// important on the MonsterASP Free plan's 256 MB memory limit.
/// </summary>
public sealed class IndiaMedicineDatasetService : IIndiaMedicineDatasetService
{
    private const string DatasetUrl =
        "https://raw.githubusercontent.com/junioralive/Indian-Medicine-Dataset/main/DATA/indian_medicine_data.csv";

    private static readonly TimeSpan DatasetRefreshAge = TimeSpan.FromDays(30);

    private readonly IWebHostEnvironment _environment;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<IndiaMedicineDatasetService> _logger;
    private readonly SemaphoreSlim _downloadLock = new(1, 1);

    public IndiaMedicineDatasetService(
        IWebHostEnvironment environment,
        IHttpClientFactory httpClientFactory,
        ILogger<IndiaMedicineDatasetService> logger)
    {
        _environment = environment;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public async Task<IReadOnlyList<IndiaMedicineDatasetItem>> SearchAsync(
        string query,
        int limit,
        CancellationToken cancellationToken)
    {
        var normalizedQuery = Normalize(query);
        if (normalizedQuery.Length < 2)
            return [];

        limit = Math.Clamp(limit, 1, 100);
        var csvPath = await EnsureDatasetAvailableAsync(cancellationToken);

        var prefixMatches = new List<IndiaMedicineDatasetItem>(limit * 4);
        var otherMatches = new List<IndiaMedicineDatasetItem>(limit * 4);
        var scannedRows = 0;

        await using var stream = new FileStream(
            csvPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 64 * 1024,
            useAsync: true);
        using var reader = new StreamReader(stream, Encoding.UTF8);
        using var csv = new CsvReader(reader, new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            HeaderValidated = null,
            MissingFieldFound = null,
            BadDataFound = null,
            TrimOptions = TrimOptions.Trim
        });

        await foreach (var row in csv.GetRecordsAsync<dynamic>(cancellationToken))
        {
            scannedRows++;

            var values = (IDictionary<string, object>)row;
            var idText = Get(values, "id");

            if (!long.TryParse(idText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id) ||
                id <= 0)
                continue;

            var productName = Get(values, "name");
            if (string.IsNullOrWhiteSpace(productName))
                continue;

            // Discontinued medicines should not be offered as current catalogue
            // suggestions. Pharmacies must still verify product status and pack.
            if (string.Equals(Get(values, "Is_discontinued"), "TRUE", StringComparison.OrdinalIgnoreCase))
                continue;

            var composition = CombineComposition(
                Get(values, "short_composition1"),
                Get(values, "short_composition2"));
            var manufacturer = Get(values, "manufacturer_name");
            var packSize = Get(values, "pack_size_label");
            var normalizedName = Normalize(productName);
            var normalizedComposition = Normalize(composition);
            var normalizedManufacturer = Normalize(manufacturer);

            var nameStartsWith = normalizedName.StartsWith(normalizedQuery, StringComparison.Ordinal);
            var matched = nameStartsWith ||
                          normalizedName.Contains(normalizedQuery, StringComparison.Ordinal) ||
                          normalizedComposition.Contains(normalizedQuery, StringComparison.Ordinal) ||
                          normalizedManufacturer.Contains(normalizedQuery, StringComparison.Ordinal);

            if (!matched)
                continue;

            var item = new IndiaMedicineDatasetItem(
                id,
                productName.Trim(),
                composition,
                manufacturer,
                packSize,
                Get(values, "type"));

            var target = nameStartsWith ? prefixMatches : otherMatches;
            if (target.Count < limit * 4)
                target.Add(item);
        }

        var results = prefixMatches
            .OrderBy(item => Normalize(item.ProductName), StringComparer.Ordinal)
            .Concat(otherMatches.OrderBy(item => Normalize(item.ProductName), StringComparer.Ordinal))
            .Take(limit)
            .ToList();

        _logger.LogInformation(
            "Indian medicine dataset search completed. QueryLength {QueryLength}; scanned {ScannedRows} rows; returned {ResultCount} results.",
            normalizedQuery.Length,
            scannedRows,
            results.Count);

        return results;
    }

    private async Task<string> EnsureDatasetAvailableAsync(CancellationToken cancellationToken)
    {
        var dataDirectory = Path.Combine(_environment.ContentRootPath, "App_Data");
        Directory.CreateDirectory(dataDirectory);

        var csvPath = Path.Combine(dataDirectory, "indian_medicine_data.csv");
        if (IsFreshDataset(csvPath))
            return csvPath;

        await _downloadLock.WaitAsync(cancellationToken);
        try
        {
            if (IsFreshDataset(csvPath))
                return csvPath;

            var tempPath = $"{csvPath}.{Guid.NewGuid():N}.tmp";

            try
            {
                _logger.LogInformation("Downloading the Indian medicine reference dataset.");

                var client = _httpClientFactory.CreateClient("IndiaMedicineDataset");
                using var response = await client.GetAsync(
                    DatasetUrl,
                    HttpCompletionOption.ResponseHeadersRead,
                    cancellationToken);

                response.EnsureSuccessStatusCode();

                await using var input = await response.Content.ReadAsStreamAsync(cancellationToken);
                await using (var output = new FileStream(
                    tempPath,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.None,
                    bufferSize: 64 * 1024,
                    useAsync: true))
                {
                    await input.CopyToAsync(output, cancellationToken);
                    await output.FlushAsync(cancellationToken);
                }

                var downloaded = new FileInfo(tempPath);
                if (downloaded.Length < 1024 * 1024)
                    throw new InvalidDataException("The Indian medicine dataset download was unexpectedly small.");

                File.Move(tempPath, csvPath, overwrite: true);

                _logger.LogInformation(
                    "Indian medicine dataset downloaded successfully. Size {FileBytes} bytes.",
                    downloaded.Length);

                return csvPath;
            }
            catch (Exception ex) when (File.Exists(csvPath))
            {
                _logger.LogWarning(
                    ex,
                    "Could not refresh the Indian medicine dataset. The existing local copy will be used.");
                return csvPath;
            }
            finally
            {
                if (File.Exists(tempPath))
                    File.Delete(tempPath);
            }
        }
        finally
        {
            _downloadLock.Release();
        }
    }

    private static bool IsFreshDataset(string path)
    {
        if (!File.Exists(path))
            return false;

        var file = new FileInfo(path);
        return file.Length >= 1024 * 1024 &&
               DateTime.UtcNow - file.LastWriteTimeUtc < DatasetRefreshAge;
    }

    private static string? Get(IDictionary<string, object> values, string key) =>
        values.TryGetValue(key, out var value)
            ? Convert.ToString(value, CultureInfo.InvariantCulture)?.Trim()
            : null;

    private static string? CombineComposition(string? first, string? second)
    {
        var parts = new[] { first, second }
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value!.Trim())
            .ToArray();

        return parts.Length == 0 ? null : string.Join(" + ", parts);
    }

    private static string Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : string.Join(" ", value.Trim().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
                .ToLowerInvariant();
}
