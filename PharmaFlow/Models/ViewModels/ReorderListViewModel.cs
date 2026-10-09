namespace PharmaFlow.Models.ViewModels;

public sealed class ReorderListViewModel
{
    public string SearchTerm { get; init; } = string.Empty;
    public int Count { get; init; }
    public IReadOnlyList<ReorderListProductViewModel> Items { get; init; } = Array.Empty<ReorderListProductViewModel>();
}

public sealed class ReorderListProductViewModel
{
    public long ProductId { get; init; }
    public string ProductName { get; init; } = string.Empty;
    public string? GenericName { get; init; }
    public string? BrandName { get; init; }
    public string? Barcode { get; init; }
    public decimal CurrentQuantity { get; init; }
    public decimal QuantityWhenAdded { get; init; }
    public DateTime AddedAt { get; init; }
}
