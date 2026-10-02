namespace PharmaFlow.Models;

public sealed class Customer
{
    public long CustomerId { get; set; }
    public long ProfileId { get; set; }
    public string? FullName { get; set; }
    public string? PhoneNumber { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
