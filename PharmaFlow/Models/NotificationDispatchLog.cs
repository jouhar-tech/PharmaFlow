using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace PharmaFlow.Models;

[Table("notification_dispatch_log", Schema = "public")]
public sealed class NotificationDispatchLog
{
    [Key]
    [Column("notification_id")]
    public long NotificationId { get; set; }

    [Column("profile_id")]
    public long ProfileId { get; set; }

    [Required]
    [Column("notification_type")]
    public string NotificationType { get; set; } = string.Empty;

    [Required]
    [Column("period_key")]
    public string PeriodKey { get; set; } = string.Empty;

    [Column("sent_at")]
    public DateTime SentAt { get; set; }

    [Required]
    [Column("status")]
    public string Status { get; set; } = "sent";

    [Column("attempted_at")]
    public DateTime? AttemptedAt { get; set; }

    [Column("attempt_token")]
    public Guid? AttemptToken { get; set; }
}
