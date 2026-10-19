using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using NoCTF.Domain.Shared;

namespace NoCTF.Domain.Competitions.Blood;

public enum BloodPosition : short { First = 1, Second = 2, Third = 3 }

[Table("competition_blood_announcements")]
public sealed class BloodAnnouncement : IConcurrencyTracked
{
    [Key] public Guid Id { get; set; }
    public Guid CompetitionId { get; set; }
    public Guid CompetitionChallengeId { get; set; }
    public BloodPosition Position { get; set; }
    public Guid? GameplayFactId { get; set; }
    public Guid? TeamId { get; set; }
    public long PublishedVersion { get; set; }
    public Guid ConcurrencyStamp { get; set; } = Guid.NewGuid();
    public Guid? LastEventId { get; set; }
}
