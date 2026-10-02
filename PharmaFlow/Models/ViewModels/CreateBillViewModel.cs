namespace PharmaFlow.Models.ViewModels;

public sealed class CreateBillViewModel
{
    public string CustomerName { get; set; } = string.Empty;
    public string PhoneNumber { get; set; } = string.Empty;
    public string PaymentMethod { get; set; } = "Cash";
    public string ItemsJson { get; set; } = string.Empty;
}

public sealed class CreateBillItemViewModel
{
    public long BatchId { get; init; }
    public string ProductName { get; init; } = string.Empty;
    public string BatchNumber { get; init; } = string.Empty;
    public DateOnly ExpiryDate { get; init; }
    public decimal Quantity { get; init; }
    public decimal Mrp { get; init; }
    public decimal UnitPrice { get; init; }
    public decimal GstRate { get; init; }
    public decimal LineTotal { get; init; }
    public decimal GstAmount { get; init; }
}
