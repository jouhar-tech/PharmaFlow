namespace PharmaFlow.Models;

public sealed class SalesBill
{
    public long BillId { get; set; }
    public long ProfileId { get; set; }
    public long? CustomerId { get; set; }
    public string BillNumber { get; set; } = string.Empty;
    public string? CustomerName { get; set; }
    public string? CustomerPhone { get; set; }
    public decimal Subtotal { get; set; }
    public decimal TaxableAmount { get; set; }
    public decimal CgstAmount { get; set; }
    public decimal SgstAmount { get; set; }
    public decimal IgstAmount { get; set; }
    public decimal GstAmount { get; set; }
    public decimal TotalAmount { get; set; }
    public string PaymentMethod { get; set; } = "Cash";
    public string Status { get; set; } = "Completed";
    public DateTime CreatedAt { get; set; }

    public ICollection<SalesBillItem> Items { get; set; } = new List<SalesBillItem>();
}
