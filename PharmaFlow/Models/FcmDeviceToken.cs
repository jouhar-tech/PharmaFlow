using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace PharmaFlow.Models;

[Table("fcm_device_tokens", Schema = "public")]
public sealed class FcmDeviceToken
{
    [Key]
    [Column("device_token_id")]
    public long DeviceTokenId { get; set; }

    [Column("profile_id")]
    public long ProfileId { get; set; }

    [Required]
    [Column("token")]
    public string Token { get; set; } = string.Empty;

    [Required]
    [Column("platform")]
    public string Platform { get; set; } = "android";

    [Column("user_agent")]
    public string? UserAgent { get; set; }

    [Column("permission_granted")]
    public bool PermissionGranted { get; set; }

    [Column("is_active")]
    public bool IsActive { get; set; } = true;

    [Column("created_at")]
    public DateTime CreatedAt { get; set; }

    [Column("updated_at")]
    public DateTime UpdatedAt { get; set; }

    [Column("last_success_at")]
    public DateTime? LastSuccessAt { get; set; }

    [Column("last_failure_at")]
    public DateTime? LastFailureAt { get; set; }
}
