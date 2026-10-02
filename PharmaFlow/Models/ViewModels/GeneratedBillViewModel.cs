namespace PharmaFlow.Models.ViewModels;

public sealed class GeneratedBillViewModel
{
    public string InvoiceNumber { get; set; } = string.Empty;
    public DateTime InvoiceDate { get; set; }
    public string? CustomerName { get; set; }
    public string? PhoneNumber { get; set; }
    public decimal TotalAmount { get; set; }
    public string PaymentMethod { get; set; } = "Cash";

    public string PaymentMethodIcon => PaymentMethod switch
    {
        "UPI" => "upi",
        "Udhaar" => "credit",
        _ => "cash"
    };
}
