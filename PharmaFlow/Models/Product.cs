namespace PharmaFlow.Models;

public sealed class Product
{
    public long ProductId { get; set; }
    public long ProfileId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public string? GenericName { get; set; }
    public string? BrandName { get; set; }
    public long? CategoryId { get; set; }
    public string? DosageForm { get; set; }
    public string? Strength { get; set; }
    public string? PackSize { get; set; }
    public string? Barcode { get; set; }
    public string? Manufacturer { get; set; }
    public string? HsnCode { get; set; }
    public decimal? GstRate { get; set; }
    public decimal ReorderLevel { get; set; }
    public bool IsPrescriptionRequired { get; set; }
    public bool IsActive { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public ICollection<ProductBatch> Batches { get; set; } = new List<ProductBatch>();
}
