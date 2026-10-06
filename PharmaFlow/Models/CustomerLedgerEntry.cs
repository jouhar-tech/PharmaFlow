namespace PharmaFlow.Models;

public sealed class CustomerLedgerEntry
{
    public long LedgerEntryId { get; set; }
    public long ProfileId { get; set; }
    public long CustomerId { get; set; }
    public long? BillId { get; set; }
    public string EntryType { get; set; } = string.Empty;
    public decimal BalanceChange { get; set; }
    public string? Description { get; set; }
    public DateTime CreatedAt { get; set; }
}
