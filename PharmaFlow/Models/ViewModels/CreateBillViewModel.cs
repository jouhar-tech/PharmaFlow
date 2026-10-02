namespace PharmaFlow.Models.ViewModels;

public sealed class CreateBillViewModel
{
    public string CustomerName { get; set; } = string.Empty;
    public string PhoneNumber { get; set; } = string.Empty;
    public string PaymentMethod { get; set; } = "Cash";
    public List<CreateBillItemViewModel> Items { get; set; } = [];
}

public sealed class CreateBillItemViewModel
{
    public long ProductId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public decimal Quantity { get; set; }
    public decimal Mrp { get; set; }
    public decimal Amount { get; set; }
}
