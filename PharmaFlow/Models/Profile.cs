using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace PharmaFlow.Models;

[Table("profiles", Schema = "public")]
public sealed class Profile
{
    [Key]
    public long Id { get; set; }

    [Column("user_id")]
    public Guid UserId { get; set; }

    [Required]
    [Column("username")]
    public string Username { get; set; } = string.Empty;

    [Column("email")]
    public string? Email { get; set; }

    [Column("business_name")]
    public string? BusinessName { get; set; }

    [Column("phone_number")]
    public string? PhoneNumber { get; set; }

    [Column("active_status")]
    public short ActiveStatus { get; set; }


    [Column("created_at")]
    public DateTime CreatedAt { get; set; }
}
