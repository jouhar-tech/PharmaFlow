namespace PharmaFlow.Models.ViewModels;

public sealed class LowStockViewModel
{
    public string SearchTerm { get; init; } = string.Empty;
    public int Count { get; init; }
    public IReadOnlyList<LowStockItemViewModel> Items { get; init; } = Array.Empty<LowStockItemViewModel>();
}

public sealed class LowStockItemViewModel
{
    public long ProductId { get; init; }
    public long BatchId { get; init; }
    public string ProductName { get; init; } = string.Empty;
    public string? GenericName { get; init; }
    public string? BrandName { get; init; }
    public string? Barcode { get; init; }
    public string BatchNumber { get; init; } = string.Empty;
    public decimal Quantity { get; init; }
    public DateOnly ExpiryDate { get; init; }
    public decimal SellingUnitPrice { get; init; }
    public decimal StockValue { get; init; }
}
