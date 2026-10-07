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
    private List<MedicineRecord>? _medicines;

    public MedicineCatalogService(
        IWebHostEnvironment environment,
        ILogger<MedicineCatalogService> logger)
    {
        _environment = environment;
        _logger = logger;
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

        return _medicines
            .Where(item =>
                Normalize(item.ProductName).Contains(normalized, StringComparison.OrdinalIgnoreCase) ||
                Normalize(item.SaltComposition).Contains(normalized, StringComparison.OrdinalIgnoreCase) ||
                Normalize(item.ManufacturerName).Contains(normalized, StringComparison.OrdinalIgnoreCase))
            .OrderBy(item => Normalize(item.ProductName).StartsWith(normalized, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .ThenBy(item => item.ProductName)
            .Take(limit)
            .ToList();
    }

    public async Task<MedicineRecord?> GetAsync(long id, CancellationToken cancellationToken)
    {
        await EnsureLoadedAsync(cancellationToken);
        return _medicines?.FirstOrDefault(item => item.Id == id);
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
                    using var client = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
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

                records.Add(new MedicineRecord
                {
                    Id = id,
                    ProductName = name,
                    Price = Get(values, "price(₹)"),
                    IsDiscontinued = discontinued,
                    ManufacturerName = Get(values, "manufacturer_name"),
                    Type = Get(values, "type"),
                    PackSizeLabel = Get(values, "pack_size_label"),
                    SaltComposition = CombineComposition(
                        Get(values, "short_composition1"),
                        Get(values, "short_composition2"))
                });
            }

            _medicines = records;
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
