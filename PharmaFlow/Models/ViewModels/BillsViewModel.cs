namespace PharmaFlow.Models.ViewModels;

public sealed class BillsViewModel
{
    public string SelectedPeriod { get; init; } = "Today";
    public string SearchTerm { get; init; } = string.Empty;
    public DateTime? StartDate { get; init; }
    public DateTime? EndDate { get; init; }
    public decimal TotalSalesAmount { get; init; }
    public int BillCount { get; init; }
    public IReadOnlyList<BillCardViewModel> Bills { get; init; } = [];
}

public sealed class BillCardViewModel
{
    public long BillId { get; init; }
    public string BillNumber { get; init; } = string.Empty;
    public DateTimeOffset CreatedAt { get; init; }
    public string? CustomerName { get; init; }
    public string? CustomerPhone { get; init; }
    public string PaymentMethod { get; init; } = "Cash";
    public decimal TotalAmount { get; init; }
    public string ProductNames { get; init; } = string.Empty;
}
