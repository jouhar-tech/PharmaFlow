namespace PharmaFlow.Models.ViewModels;

public sealed class GeneratedBillViewModel
{
    public long BillId { get; init; }
    public string BusinessName { get; init; } = "PharmaFlow";
    public string? BusinessPhone { get; init; }
    public string? BusinessEmail { get; init; }
    public string InvoiceNumber { get; init; } = string.Empty;
    public DateTime InvoiceDate { get; init; }
    public string? CustomerName { get; init; }
    public string? PhoneNumber { get; init; }
    public decimal Subtotal { get; init; }
    public decimal TaxableAmount { get; init; }
    public decimal CgstAmount { get; init; }
    public decimal SgstAmount { get; init; }
    public decimal IgstAmount { get; init; }
    public decimal GstAmount { get; init; }
    public decimal TotalAmount { get; init; }
    public string PaymentMethod { get; init; } = "Cash";
    public IReadOnlyList<GeneratedBillLineViewModel> Items { get; init; } = [];

    public string PaymentMethodIcon => PaymentMethod switch
    {
        "UPI" => "upi",
        "Udhaar" => "credit",
        _ => "cash"
    };
}

public sealed class GeneratedBillLineViewModel
{
    public string ProductName { get; init; } = string.Empty;
    public string BatchNumber { get; init; } = string.Empty;
    public decimal Quantity { get; init; }
    public decimal UnitPrice { get; init; }
    public decimal Mrp { get; init; }
    public decimal GstRate { get; init; }
    public decimal GstAmount { get; init; }
    public decimal LineTotal { get; init; }
}
