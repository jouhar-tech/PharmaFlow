namespace PharmaFlow.Models;

public sealed class InvoiceImportItem
{
    public long ImportItemId { get; set; }
    public long ImportId { get; set; }
    public int RowNumber { get; set; }
    public string? RawLine { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public string BatchNumber { get; set; } = string.Empty;
    public DateOnly? ExpiryDate { get; set; }
    public decimal? Quantity { get; set; }
    public decimal? Mrp { get; set; }
    public decimal Confidence { get; set; }
    public string ValidationStatus { get; set; } = "needs_review";
    public string? ValidationMessage { get; set; }
    public long? MatchedProductId { get; set; }
    public long? SavedBatchId { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public InvoiceImport Import { get; set; } = null!;
    public Product? MatchedProduct { get; set; }
    public ProductBatch? SavedBatch { get; set; }
}
