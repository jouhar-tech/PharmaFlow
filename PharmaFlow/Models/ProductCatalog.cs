namespace PharmaFlow.Models;

public sealed class ProductCatalog
{
    public long CatalogId { get; set; }
    public string Source { get; set; } = string.Empty;
    public string ExternalId { get; set; } = string.Empty;
    public string ProductType { get; set; } = string.Empty;
    public string ProductName { get; set; } = string.Empty;
    public string? GenericName { get; set; }
    public string? BrandName { get; set; }
    public string? Manufacturer { get; set; }
    public string? DosageForm { get; set; }
    public string? Strength { get; set; }
    public string? PackSize { get; set; }
    public string? Barcode { get; set; }
    public string? HsnCode { get; set; }
    public decimal? GstRate { get; set; }
    public bool IsPrescriptionRequired { get; set; }
    public string? SourceUrl { get; set; }
    public DateTime FirstSeenAt { get; set; }
    public DateTime LastSyncedAt { get; set; }
    public DateTime CacheExpiresAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
