namespace PharmaFlow.Models.ViewModels;

public sealed class BillDetailsViewModel
{
    public long BillId { get; init; }
    public string BusinessName { get; init; } = "PharmaFlow";
    public string? BusinessPhone { get; init; }
    public string? BusinessEmail { get; init; }
    public string InvoiceNumber { get; init; } = string.Empty;
    public DateTime InvoiceDate { get; init; }
    public string? CustomerName { get; init; }
    public string? PhoneNumber { get; init; }
    public string? PaymentMethod { get; init; }
    public decimal TotalAmount { get; init; }
    public IReadOnlyList<BillDetailsLineViewModel> Items { get; init; } = [];
}

public sealed class BillDetailsLineViewModel
{
    public string ProductName { get; init; } = string.Empty;
    public string BatchNumber { get; init; } = string.Empty;
    public decimal Quantity { get; init; }
    public decimal LineTotal { get; init; }
}
