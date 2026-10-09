namespace PharmaFlow.Models;

public sealed class ReorderListItem
{
    public long ReorderListItemId { get; set; }
    public long ProfileId { get; set; }
    public long ProductId { get; set; }
    public decimal QuantityWhenAdded { get; set; }
    public DateTime CreatedAt { get; set; }

    public Product Product { get; set; } = null!;
}
