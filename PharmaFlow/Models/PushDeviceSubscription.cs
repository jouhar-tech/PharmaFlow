using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace PharmaFlow.Models;

[Table("push_device_subscriptions", Schema = "public")]
public sealed class PushDeviceSubscription
{
    [Key]
    [Column("subscription_id")]
    public long SubscriptionId { get; set; }

    [Column("profile_id")]
    public long ProfileId { get; set; }

    [Column("user_role")]
    public string UserRole { get; set; } = "Owner";

    [Required]
    [Column("endpoint")]
    public string Endpoint { get; set; } = string.Empty;

    [Required]
    [Column("p256dh")]
    public string P256dh { get; set; } = string.Empty;

    [Required]
    [Column("auth")]
    public string Auth { get; set; } = string.Empty;

    [Column("user_agent")]
    public string? UserAgent { get; set; }

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
