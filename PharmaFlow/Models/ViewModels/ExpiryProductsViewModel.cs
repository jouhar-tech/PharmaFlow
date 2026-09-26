namespace PharmaFlow.Models.ViewModels;

public sealed class ExpiringProductsViewModel
{
    public int CriticalCount { get; init; }
    public int UpcomingCount { get; init; }
    public decimal AtRiskValue { get; init; }
    public IReadOnlyList<ExpiryProductItemViewModel> Products { get; init; } = Array.Empty<ExpiryProductItemViewModel>();
    public string ViewMode { get; init; } = "overview";
}

public sealed class ExpiryProductItemViewModel
{
    public long ProductId { get; init; }
    public long BatchId { get; init; }
    public string ProductName { get; init; } = string.Empty;
    public string BatchNumber { get; init; } = string.Empty;
    public DateTime ExpiryDate { get; init; }
    public int DaysLeft { get; init; }
    public decimal Quantity { get; init; }
    public decimal PurchaseUnitPrice { get; init; }
    public decimal TotalValue { get; init; }
}
