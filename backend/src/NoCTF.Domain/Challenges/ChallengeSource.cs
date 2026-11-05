using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace NoCTF.Domain.Challenges;

/// <summary>Copies retain opaque provenance after the original material is removed.</summary>
[Table("challenge_sources")]
public sealed class ChallengeSource
{
    [Key] public Guid ChallengeId { get; set; }
    public Guid OriginalChallengeId { get; set; }
    public Guid CanonicalChallengeId { get; set; }
    public DateTimeOffset CopiedAt { get; set; }
}
