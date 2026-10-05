namespace PharmaFlow.Models.ViewModels;

public sealed class InventoryProductDetailsViewModel
{
    public long ProductId { get; init; }
    public string ProductName { get; init; } = string.Empty;
    public string? GenericName { get; init; }
    public string? BrandName { get; init; }
    public string? DosageForm { get; init; }
    public string? Strength { get; init; }
    public string? PackSize { get; init; }
    public string? Barcode { get; init; }
    public string? Manufacturer { get; init; }
    public string? HsnCode { get; init; }
    public decimal? GstRate { get; init; }
    public decimal ReorderLevel { get; init; }
    public bool IsPrescriptionRequired { get; init; }
    public bool IsActive { get; init; }
    public InventoryBatchDetailsViewModel? SelectedBatch { get; init; }
    public IReadOnlyList<InventoryBatchDetailsViewModel> Batches { get; init; } = [];
}

public sealed class InventoryBatchDetailsViewModel
{
    public long BatchId { get; init; }
    public string BatchNumber { get; init; } = string.Empty;
    public DateOnly? ManufacturingDate { get; init; }
    public DateOnly ExpiryDate { get; init; }
    public decimal QuantityOnHand { get; init; }
    public decimal PurchaseUnitPrice { get; init; }
    public decimal SellingUnitPrice { get; init; }
    public long? SupplierId { get; init; }
    public string? Location { get; init; }
    public bool IsQuarantined { get; init; }
    public bool IsActive { get; init; }
}
