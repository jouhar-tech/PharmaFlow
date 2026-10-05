namespace PharmaFlow.Models.ViewModels;

public sealed class PurchaseDistributorViewModel
{
    public string Name { get; init; } = string.Empty;
    public int InvoiceCount { get; init; }
    public decimal TotalAmount { get; init; }
}

public sealed class PurchaseListViewModel
{
    public string Period { get; init; } = "all";
    public DateOnly? FromDate { get; init; }
    public DateOnly? ToDate { get; init; }
    public decimal TotalPurchasedAmount { get; init; }
    public int InvoiceCount { get; init; }
    public IReadOnlyList<PurchaseDistributorViewModel> Distributors { get; init; } = [];
}

public sealed class PurchaseDistributorDetailsViewModel
{
    public string DistributorName { get; init; } = string.Empty;
    public decimal TotalAmount { get; init; }
    public int InvoiceCount { get; init; }
    public IReadOnlyList<PurchaseInvoiceViewModel> Invoices { get; init; } = [];
}

public sealed class PurchaseInvoiceViewModel
{
    public long ImportId { get; init; }
    public string InvoiceNumber { get; init; } = string.Empty;
    public DateOnly? InvoiceDate { get; init; }
    public decimal TotalAmount { get; init; }
    public IReadOnlyList<PurchaseInvoiceItemViewModel> Items { get; init; } = [];
}

public sealed class PurchaseInvoiceItemViewModel
{
    public string ProductName { get; init; } = string.Empty;
    public string BatchNumber { get; init; } = string.Empty;
    public decimal Quantity { get; init; }
    public decimal? Mrp { get; init; }
    public DateOnly? ExpiryDate { get; init; }
}
