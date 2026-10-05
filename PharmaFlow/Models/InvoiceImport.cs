namespace PharmaFlow.Models;

public sealed class InvoiceImport
{
    public long ImportId { get; set; }
    public long ProfileId { get; set; }
    public string OriginalFileName { get; set; } = string.Empty;
    public string SourceType { get; set; } = "upload";
    public string? DistributorName { get; set; }
    public string? InvoiceNumber { get; set; }
    public DateOnly? InvoiceDate { get; set; }
    public decimal? TotalAmount { get; set; }
    public string? RawOcrText { get; set; }
    public decimal OcrConfidence { get; set; }
    public string Status { get; set; } = "draft";
    public string? ErrorMessage { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public ICollection<InvoiceImportItem> Items { get; set; } = new List<InvoiceImportItem>();
}
