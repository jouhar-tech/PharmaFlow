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

public sealed class BillingCatalogSearchResultViewModel
{
    public long? CatalogId { get; init; }
    public string Source { get; init; } = string.Empty;
    public string ExternalId { get; init; } = string.Empty;
    public string ProductType { get; init; } = string.Empty;
    public string ProductName { get; init; } = string.Empty;
    public string? GenericName { get; init; }
    public string? BrandName { get; init; }
    public string? Manufacturer { get; init; }
    public string? DosageForm { get; init; }
    public string? Strength { get; init; }
    public string? PackSize { get; init; }
    public string? Barcode { get; init; }
    public decimal? GstRate { get; init; }
    public bool IsPrescriptionRequired { get; init; }
}

public sealed class BillingAddCatalogStockRequest
{
    public long? CatalogId { get; set; }
    public string Source { get; set; } = string.Empty;
    public string ExternalId { get; set; } = string.Empty;
    public string BatchNumber { get; set; } = string.Empty;
    public DateOnly ExpiryDate { get; set; }
    public decimal AvailableQuantity { get; set; }
    public decimal QuantityToBill { get; set; }
    public decimal? PurchaseUnitPrice { get; set; }
    public decimal SellingUnitPrice { get; set; }
    public decimal? GstRate { get; set; }
}
