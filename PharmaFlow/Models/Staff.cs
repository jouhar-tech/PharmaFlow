using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace PharmaFlow.Models;

[Table("staff", Schema = "public")]
public sealed class Staff
{
    [Key]
    [Column("staff_id")]
    public long StaffId { get; set; }

    [Column("profile_id")]
    public long ProfileId { get; set; }

    [Column("auth_user_id")]
    public Guid AuthUserId { get; set; }

    [Required]
    [Column("full_name")]
    public string FullName { get; set; } = string.Empty;

    [Column("address")]
    public string? Address { get; set; }

    [Column("phone_number")]
    public string? PhoneNumber { get; set; }

    [Column("email")]
    public string? Email { get; set; }

    [Column("profile_photo_url")]
    public string? ProfilePhotoUrl { get; set; }

    [Column("is_active")]
    public bool IsActive { get; set; } = true;

    [Column("last_login_at")]
    public DateTime? LastLoginAt { get; set; }

    [Column("last_logout_at")]
    public DateTime? LastLogoutAt { get; set; }

    [Column("created_at")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public Profile? Profile { get; set; }
}
