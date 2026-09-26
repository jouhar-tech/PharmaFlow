namespace PharmaFlow.Models;

public sealed class ProductBatch
{
    public long BatchId { get; set; }
    public long ProductId { get; set; }
    public string BatchNumber { get; set; } = string.Empty;
    public DateOnly? ManufacturingDate { get; set; }
    public DateOnly ExpiryDate { get; set; }
    public decimal QuantityOnHand { get; set; }
    public decimal PurchaseUnitPrice { get; set; }
    public decimal SellingUnitPrice { get; set; }
    public long? SupplierId { get; set; }
    public string? Location { get; set; }
    public bool IsQuarantined { get; set; }
    public bool IsActive { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public Product Product { get; set; } = null!;
}
