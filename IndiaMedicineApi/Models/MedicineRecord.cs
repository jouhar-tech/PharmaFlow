namespace IndiaMedicineApi.Models;

public sealed class MedicineRecord
{
    public long Id { get; init; }
    public string ProductName { get; init; } = string.Empty;
    public string? Price { get; init; }
    public bool IsDiscontinued { get; init; }
    public string? ManufacturerName { get; init; }
    public string? Type { get; init; }
    public string? PackSizeLabel { get; init; }
    public string? SaltComposition { get; init; }
}
