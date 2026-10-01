using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace PharmaFlow.Models;

[Table("pharmaflow_savings_events", Schema = "public")]
public sealed class PharmaFlowSavingsEvent
{
    [Key]
    [Column("event_id")]
    public long EventId { get; set; }

    [Column("profile_id")]
    public long ProfileId { get; set; }

    [Required]
    [Column("category")]
    public string Category { get; set; } = string.Empty;

    [Column("amount")]
    public decimal Amount { get; set; }

    [Column("description")]
    public string? Description { get; set; }

    [Column("source_type")]
    public string? SourceType { get; set; }

    [Column("source_id")]
    public string? SourceId { get; set; }

    [Column("occurred_at")]
    public DateTime OccurredAt { get; set; }
}
