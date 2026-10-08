namespace PharmaFlow.Models.ViewModels;

public sealed class SlowMovingViewModel
{
    public DateOnly Today { get; init; }
    public DateOnly OneMonthCutoffDate { get; init; }
    public DateOnly ThreeMonthCutoffDate { get; init; }
    public DateOnly SixMonthCutoffDate { get; init; }
    public DateOnly OneYearCutoffDate { get; init; }
    public DateOnly OneYearExpiryLimitDate { get; init; }

    public int AllCount { get; init; }
    public int ThreeMonthCount { get; init; }
    public int SixMonthCount { get; init; }
    public int OneYearCount { get; init; }
    public decimal TotalStuckAmount { get; init; }

    public IReadOnlyList<SlowMovingProductItemViewModel> Items { get; init; } =
        Array.Empty<SlowMovingProductItemViewModel>();
}

public sealed class SlowMovingProductItemViewModel
{
    public long ProductId { get; init; }
    public string ProductName { get; init; } = string.Empty;
    public string? GenericName { get; init; }
    public string? BrandName { get; init; }
    public string? Barcode { get; init; }
    public decimal Quantity { get; init; }
    public int BatchCount { get; init; }
    public decimal StockValue { get; init; }
    public DateOnly AddedToInventoryDate { get; init; }
    public int DaysInInventory { get; init; }
    public DateOnly EarliestExpiryDate { get; init; }
}
