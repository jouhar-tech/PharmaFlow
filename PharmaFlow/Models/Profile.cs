namespace PharmaFlow.Models;

public sealed class Profile
{
    public long Id { get; set; }
    public Guid UserId { get; set; }
    public string? FullName { get; set; }
    public string? PhoneNumber { get; set; }
    public DateTime CreatedAt { get; set; }
}
