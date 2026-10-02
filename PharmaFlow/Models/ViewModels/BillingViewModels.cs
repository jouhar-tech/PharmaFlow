namespace PharmaFlow.Models.ViewModels;

public sealed class BillingSearchResultViewModel
{
    public long ProductId { get; init; }
    public string ProductName { get; init; } = string.Empty;
    public string? HsnCode { get; init; }
    public decimal GstRate { get; init; }
    public IReadOnlyList<BillingBatchViewModel> Batches { get; init; } = [];
}

public sealed class BillingBatchViewModel
{
    public long BatchId { get; init; }
    public string BatchNumber { get; init; } = string.Empty;
    public DateOnly ExpiryDate { get; init; }
    public decimal QuantityOnHand { get; init; }
    public decimal SellingUnitPrice { get; init; }
    public decimal Mrp { get; init; }
}

public sealed class BillingLineInput
{
    public long BatchId { get; set; }
    public decimal Quantity { get; set; }
}

