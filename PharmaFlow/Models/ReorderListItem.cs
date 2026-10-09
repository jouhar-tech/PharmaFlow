namespace PharmaFlow.Models;

public sealed class ReorderListItem
{
    public long ReorderListItemId { get; set; }
    public long ProfileId { get; set; }

    // Inventory-backed entries use ProductId. Catalog-only entries keep ProductId null.
    public long? ProductId { get; set; }
    public long? CatalogId { get; set; }
    public string? Source { get; set; }
    public string? ExternalId { get; set; }

    // Snapshot metadata lets a catalog product remain on the reorder list
    // without creating a Product or ProductBatch in inventory.
    public string? ProductName { get; set; }
    public string? GenericName { get; set; }
    public string? BrandName { get; set; }
    public string? Barcode { get; set; }

    public decimal QuantityWhenAdded { get; set; }
    public DateTime CreatedAt { get; set; }

    public Product? Product { get; set; }
}
