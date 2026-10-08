namespace PharmaFlow.Models.ViewModels;

public sealed class InventoryViewModel
{
    public int AllCount { get; init; }
    public int LowStockCount { get; init; }
    public int ExpiringCount { get; init; }
    public decimal TotalInventoryValue { get; init; }
    public string ActiveFilter { get; init; } = "all";
    public string SearchTerm { get; init; } = string.Empty;
    public IReadOnlyList<InventoryItemViewModel> Items { get; init; } = Array.Empty<InventoryItemViewModel>();
}

public sealed class InventoryItemViewModel
{
    public long ProductId { get; init; }
    public long BatchId { get; init; }
    public string ProductName { get; init; } = string.Empty;
    public string? GenericName { get; init; }
    public string? BrandName { get; init; }
    public string? Barcode { get; init; }
    public string BatchNumber { get; init; } = string.Empty;
    public decimal Quantity { get; init; }
    public decimal ReorderLevel { get; init; }
    public DateOnly ExpiryDate { get; init; }
    public int DaysLeft { get; init; }
    public decimal SellingUnitPrice { get; init; }
    public decimal StockValue { get; init; }
    public bool IsLowStock { get; init; }
    public bool IsExpiring { get; init; }
}
