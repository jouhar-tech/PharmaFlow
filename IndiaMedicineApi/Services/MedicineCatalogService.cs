using System.Globalization;
using System.Text;
using CsvHelper;
using CsvHelper.Configuration;
using IndiaMedicineApi.Models;

namespace IndiaMedicineApi.Services;

public sealed class MedicineCatalogService
{
    private const string DatasetUrl =
        "https://raw.githubusercontent.com/junioralive/Indian-Medicine-Dataset/main/DATA/indian_medicine_data.csv";

    private readonly SemaphoreSlim _loadLock = new(1, 1);
    private readonly IWebHostEnvironment _environment;
    private readonly ILogger<MedicineCatalogService> _logger;
    private readonly IHttpClientFactory _httpClientFactory;
    private List<MedicineRecord>? _medicines;
    private Dictionary<long, MedicineRecord>? _medicinesById;

    public MedicineCatalogService(
        IWebHostEnvironment environment,
        ILogger<MedicineCatalogService> logger,
        IHttpClientFactory httpClientFactory)
    {
        _environment = environment;
        _logger = logger;
        _httpClientFactory = httpClientFactory;
    }

    public async Task<IReadOnlyList<MedicineRecord>> SearchAsync(
        string query,
        int limit,
        CancellationToken cancellationToken)
    {
        await EnsureLoadedAsync(cancellationToken);

        var normalized = Normalize(query);
        if (normalized.Length < 2 || _medicines is null)
            return [];

        var prefixMatches = new List<MedicineRecord>(limit);
        var fallbackMatches = new List<MedicineRecord>(limit);

        foreach (var item in _medicines)
        {
            var productNameMatch = item.NormalizedProductName.Contains(
                normalized,
                StringComparison.Ordinal);
            var saltMatch = item.NormalizedSaltComposition.Contains(
                normalized,
                StringComparison.Ordinal);
            var manufacturerMatch = item.NormalizedManufacturerName.Contains(
                normalized,
                StringComparison.Ordinal);

            if (!productNameMatch && !saltMatch && !manufacturerMatch)
                continue;

            if (item.NormalizedProductName.StartsWith(normalized, StringComparison.Ordinal))
            {
                prefixMatches.Add(item);
                if (prefixMatches.Count == limit)
                    return prefixMatches;
            }
            else if (fallbackMatches.Count < limit)
            {
                fallbackMatches.Add(item);
            }
        }

        prefixMatches.AddRange(fallbackMatches);
        return prefixMatches.Take(limit).ToList();
    }

    public async Task<MedicineRecord?> GetAsync(long id, CancellationToken cancellationToken)
    {
        await EnsureLoadedAsync(cancellationToken);
        return _medicinesById is not null && _medicinesById.TryGetValue(id, out var medicine)
            ? medicine
            : null;
    }

    private async Task EnsureLoadedAsync(CancellationToken cancellationToken)
    {
        if (_medicines is not null)
            return;

        await _loadLock.WaitAsync(cancellationToken);
        try
        {
            if (_medicines is not null)
                return;

            var dataDirectory = Path.Combine(_environment.ContentRootPath, "App_Data");
            Directory.CreateDirectory(dataDirectory);

            var csvPath = Path.Combine(dataDirectory, "indian_medicine_data.csv");

            if (!File.Exists(csvPath))
            {
                _logger.LogInformation("Downloading Indian medicine dataset...");
                var tempPath = $"{csvPath}.{Guid.NewGuid():N}.tmp";

                try
                {
                    var client = _httpClientFactory.CreateClient("Dataset");
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
                        bufferSize: 1024 * 64,
                        useAsync: true))
                    {
                        await input.CopyToAsync(output, cancellationToken);
                    }

                    File.Move(tempPath, csvPath);
                }
                finally
                {
                    if (File.Exists(tempPath))
                        File.Delete(tempPath);
                }
            }

            _logger.LogInformation("Loading Indian medicine dataset from {Path}.", csvPath);

            using var stream = File.OpenRead(csvPath);
            using var reader = new StreamReader(stream, Encoding.UTF8);
            using var csv = new CsvReader(reader, new CsvConfiguration(CultureInfo.InvariantCulture)
            {
                HeaderValidated = null,
                MissingFieldFound = null,
                BadDataFound = null,
                TrimOptions = TrimOptions.Trim
            });

            var records = new List<MedicineRecord>();

            await foreach (var row in csv.GetRecordsAsync<dynamic>(cancellationToken))
            {
                var values = (IDictionary<string, object>)row;

                var idText = Get(values, "id");
                if (!long.TryParse(idText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id) || id <= 0)
                    continue;

                var name = Get(values, "name");
                if (string.IsNullOrWhiteSpace(name))
                    continue;

                var discontinued = string.Equals(
                    Get(values, "Is_discontinued"),
                    "TRUE",
                    StringComparison.OrdinalIgnoreCase);

                var manufacturer = Get(values, "manufacturer_name");
                var composition = CombineComposition(
                    Get(values, "short_composition1"),
                    Get(values, "short_composition2"));

                records.Add(new MedicineRecord
                {
                    Id = id,
                    ProductName = name,
                    Price = Get(values, "price(₹)"),
                    IsDiscontinued = discontinued,
                    ManufacturerName = manufacturer,
                    Type = Get(values, "type"),
                    PackSizeLabel = Get(values, "pack_size_label"),
                    SaltComposition = composition,
                    NormalizedProductName = Normalize(name),
                    NormalizedSaltComposition = Normalize(composition),
                    NormalizedManufacturerName = Normalize(manufacturer)
                });
            }

            records.Sort((left, right) =>
                string.Compare(
                    left.NormalizedProductName,
                    right.NormalizedProductName,
                    StringComparison.Ordinal));

            _medicines = records;
            _medicinesById = records.ToDictionary(item => item.Id);
            _logger.LogInformation("Loaded {Count} Indian medicines.", records.Count);
        }
        finally
        {
            _loadLock.Release();
        }
    }

    private static string? Get(IDictionary<string, object> values, string key) =>
        values.TryGetValue(key, out var value)
            ? Convert.ToString(value, CultureInfo.InvariantCulture)?.Trim()
            : null;

    private static string? CombineComposition(string? first, string? second)
    {
        var values = new[] { first, second }
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value!.Trim())
            .ToArray();

        return values.Length == 0 ? null : string.Join(" + ", values);
    }

    private static string Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : string.Join(" ", value.Trim().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
                .ToLowerInvariant();
}
