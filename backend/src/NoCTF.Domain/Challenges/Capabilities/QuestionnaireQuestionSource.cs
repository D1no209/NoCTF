using NoCTF.Domain.Shared;

namespace NoCTF.Domain.Challenges.Capabilities;

public sealed class QuestionnaireQuestionSource : IConcurrencyTracked
{
    public Guid Id { get; set; }
    public Guid ConcurrencyStamp { get; set; } = Guid.NewGuid();
    public DateTimeOffset? FirstExposedAt { get; set; }
}
