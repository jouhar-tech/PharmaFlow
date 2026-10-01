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
}
