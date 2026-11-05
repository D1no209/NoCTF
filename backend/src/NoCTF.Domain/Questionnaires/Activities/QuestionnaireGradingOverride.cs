using NoCTF.Domain.Shared;

namespace NoCTF.Domain.Questionnaires.Activities;

public enum QuestionnaireCorrectionMode : short { ReplaceAnswer, FullCredit }

public sealed class QuestionnaireGradingOverride : IConcurrencyTracked
{
    public Guid ActivityId { get; set; }
    public Guid RevisionId { get; set; }
    public Guid ConcurrencyStamp { get; set; } = Guid.NewGuid();
    public Guid TargetRevision { get; set; }
    public Guid? AppliedRevision { get; set; }
    public QuestionnaireCorrectionMode Mode { get; set; }
    public List<QuestionnaireGradingOption> Options { get; set; } = [];
}

public sealed class QuestionnaireGradingOption
{
    public Guid ActivityId { get; set; }
    public Guid RevisionId { get; set; }
    public Guid OptionId { get; set; }
}
