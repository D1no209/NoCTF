using NoCTF.Domain.Shared;

namespace NoCTF.Domain.Challenges.Capabilities;

public sealed class QuestionnaireAssignment : IConcurrencyTracked
{
    public Guid Id { get; set; }
    public Guid ConcurrencyStamp { get; set; } = Guid.NewGuid();
    public Guid SessionId { get; set; }
    public Guid ChallengeId { get; set; }
    public Guid RevisionId { get; set; }
    public NoCTF.Domain.Gameplay.GameplayFact? Fact { get; set; }
}
