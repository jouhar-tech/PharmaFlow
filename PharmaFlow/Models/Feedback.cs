using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace PharmaFlow.Models;

[Table("feedback", Schema = "public")]
public sealed class Feedback
{
    [Key]
    public long Id { get; set; }

    [Column("profile_id")]
    public long ProfileId { get; set; }

    [Required]
    [Column("message")]
    public string Message { get; set; } = string.Empty;

    [Column("created_at")]
    public DateTime CreatedAt { get; set; }
}
