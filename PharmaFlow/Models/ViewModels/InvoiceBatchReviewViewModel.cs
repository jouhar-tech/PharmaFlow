namespace PharmaFlow.Models.ViewModels;

public sealed class InvoiceBatchReviewViewModel
{
    public IReadOnlyList<InvoiceBatchReviewItemViewModel> Invoices { get; init; } = [];
}

public sealed class InvoiceBatchReviewItemViewModel
{
    public long ImportId { get; init; }
    public int InvoiceNumber { get; set; }
    public string OriginalFileName { get; init; } = string.Empty;
    public string SourceType { get; init; } = string.Empty;
    public decimal OcrConfidence { get; init; }
    public int ItemCount { get; init; }
    public string Status { get; init; } = string.Empty;
}
