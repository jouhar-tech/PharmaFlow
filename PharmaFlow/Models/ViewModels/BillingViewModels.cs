namespace PharmaFlow.Models.ViewModels;

public sealed class BillingSearchResultViewModel
{
    public long ProductId { get; init; }
    public string ProductName { get; init; } = string.Empty;
    public string? HsnCode { get; init; }
    public decimal GstRate { get; init; }
    public IReadOnlyList<BillingBatchViewModel> Batches { get; init; } = [];
}

public sealed class BillingBatchViewModel
{
    public long BatchId { get; init; }
    public string BatchNumber { get; init; } = string.Empty;
    public DateOnly ExpiryDate { get; init; }
    public decimal QuantityOnHand { get; init; }
    public decimal SellingUnitPrice { get; init; }
    public decimal Mrp { get; init; }
}

public sealed class BillingLineInput
{
    public long BatchId { get; set; }
    public decimal Quantity { get; set; }
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
}
