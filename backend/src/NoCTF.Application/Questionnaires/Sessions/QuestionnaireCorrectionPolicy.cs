using NoCTF.Domain.Questionnaires.Activities;
using NoCTF.Domain.Questionnaires.Sessions;
using NoCTF.Domain.Challenges.Capabilities;

namespace NoCTF.Application.Questionnaires.Sessions;

public static class QuestionnaireCorrectionPolicy
{
    public static bool IsValid(QuestionnaireQuestion question, QuestionnaireCorrectionMode mode, IReadOnlyCollection<Guid> ids)
    {
        if (!Enum.IsDefined(mode)) return false;
        if (mode == QuestionnaireCorrectionMode.FullCredit) return ids.Count == 0;
        if (ids.Distinct().Count() != ids.Count || ids.Any(id => question.Options.All(option => option.Id != id))) return false;
        return question.Kind == QuestionnaireQuestionKind.MultipleChoice ? ids.Count >= 2 : ids.Count == 1;
    }

    public static void Apply(QuestionnaireSessionQuestion item, QuestionnaireGradingOverride correction)
    {
        if (item.RevisionId != correction.RevisionId) throw new InvalidOperationException("Grading override does not belong to the issued version.");
        item.Result = correction.Mode == QuestionnaireCorrectionMode.FullCredit ? QuestionnaireAnswerResult.FullCredit
            : !item.Options.Any(option => option.Selected) ? QuestionnaireAnswerResult.Unanswered
            : item.Options.Where(option => option.Selected).Select(option => option.OptionId).ToHashSet()
                .SetEquals(correction.Options.Select(option => option.OptionId)) ? QuestionnaireAnswerResult.Correct : QuestionnaireAnswerResult.Wrong;
        item.AppliedGradingRevision = correction.TargetRevision;
    }
}
