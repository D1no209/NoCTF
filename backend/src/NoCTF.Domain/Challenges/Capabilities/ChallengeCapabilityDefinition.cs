using NoCTF.Domain.Shared;

namespace NoCTF.Domain.Challenges.Capabilities;

public enum ChallengeCapabilityKind : short { Break, Fix, Check, Control, Questionnaire }

[PersistentHierarchy]
public abstract class ChallengeCapabilityDefinition
{
    protected ChallengeCapabilityDefinition(ChallengeCapabilityKind kind) => Kind = kind;
    public Guid ChallengeId { get; set; }
    public ChallengeCapabilityKind Kind { get; private set; }
}
