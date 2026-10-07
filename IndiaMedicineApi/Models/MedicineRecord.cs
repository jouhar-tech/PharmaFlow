using System.Text.Json.Serialization;

namespace IndiaMedicineApi.Models;

public sealed class MedicineRecord
{
    [JsonPropertyName("id")]
    public long Id { get; init; }

    [JsonPropertyName("product_name")]
    public string ProductName { get; init; } = string.Empty;

    [JsonIgnore]
    internal string NormalizedProductName { get; init; } = string.Empty;

    [JsonIgnore]
    internal string NormalizedSaltComposition { get; init; } = string.Empty;

    [JsonIgnore]
    internal string NormalizedManufacturerName { get; init; } = string.Empty;

    [JsonPropertyName("price")]
    public string? Price { get; init; }

    [JsonPropertyName("is_discontinued")]
    public bool IsDiscontinued { get; init; }

    [JsonPropertyName("manufacturer_name")]
    public string? ManufacturerName { get; init; }

    [JsonPropertyName("type")]
    public string? Type { get; init; }

    [JsonPropertyName("pack_size_label")]
    public string? PackSizeLabel { get; init; }

    [JsonPropertyName("salt_composition")]
    public string? SaltComposition { get; init; }

    [JsonPropertyName("dosage_form")]
    public string? DosageForm => null;

    [JsonPropertyName("is_prescription_required")]
    public bool IsPrescriptionRequired => false;
}
