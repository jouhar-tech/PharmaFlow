namespace PharmaFlow.Models;

public sealed class CustomerReminder
{
    public long ReminderId { get; set; }
    public long ProfileId { get; set; }
    public long CustomerId { get; set; }
    public long? CatalogId { get; set; }
    public string? ProductSource { get; set; }
    public string? ProductExternalId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public string? GenericName { get; set; }
    public string? BrandName { get; set; }
    public string? Manufacturer { get; set; }
    public string? DosageForm { get; set; }
    public string? Strength { get; set; }
    public string? PackSize { get; set; }
    public string? Barcode { get; set; }
    public string? Note { get; set; }
    public DateOnly? ReminderDate { get; set; }
    public string Status { get; set; } = "Pending";
    public DateTime? CompletedAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
