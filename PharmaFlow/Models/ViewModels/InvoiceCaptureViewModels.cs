namespace PharmaFlow.Models.ViewModels;

public sealed class InvoiceReviewViewModel
{
    public long ImportId { get; set; }
    public string OriginalFileName { get; set; } = string.Empty;
    public string SourceType { get; set; } = "upload";
    public string DistributorName { get; set; } = string.Empty;
    public string InvoiceNumber { get; set; } = string.Empty;
    public DateOnly? InvoiceDate { get; set; }
    public decimal? TotalAmount { get; set; }
    public decimal OcrConfidence { get; set; }
    public string Status { get; set; } = "draft";
    public string? ErrorMessage { get; set; }
    public bool Saved => string.Equals(Status, "saved", StringComparison.OrdinalIgnoreCase);
    public List<InvoiceReviewItemViewModel> Items { get; set; } = [];
}

public sealed class InvoiceReviewItemViewModel
{
    public long ImportItemId { get; set; }
    public int RowNumber { get; set; }
    public string RawLine { get; set; } = string.Empty;
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
    public bool Removed { get; set; }
    public bool IsValid =>
        !Removed &&
        !string.IsNullOrWhiteSpace(ProductName) &&
        !string.IsNullOrWhiteSpace(BatchNumber) &&
        ExpiryDate.HasValue &&
        Quantity is > 0;
}

public sealed class InvoiceParseRequest
{
    public string OriginalFileName { get; set; } = string.Empty;
    public string SourceType { get; set; } = "upload";
    public decimal OcrConfidence { get; set; }
    public string OcrText { get; set; } = string.Empty;
    public List<InvoiceOcrLineInput> Lines { get; set; } = [];
    public List<InvoiceOcrWordInput> Words { get; set; } = [];
}

public sealed class InvoiceOcrLineInput
{
    public string Text { get; set; } = string.Empty;
    public decimal Confidence { get; set; }
}

public sealed class InvoiceOcrWordInput
{
    public string Text { get; set; } = string.Empty;
    public decimal Confidence { get; set; }
    public int X0 { get; set; }
    public int Y0 { get; set; }
    public int X1 { get; set; }
    public int Y1 { get; set; }
    public int Page { get; set; } = 1;
}
