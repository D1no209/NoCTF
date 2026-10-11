using NoCTF.Domain.Theory.Activities;
using NoCTF.Domain.Theory.Papers;
using NoCTF.Domain.Theory.Questions;

namespace NoCTF.Application.Theory.Papers;

public static class TheoryCorrectionPolicy
{
    public static bool IsValid(TheoryQuestionRevision question, TheoryCorrectionMode mode, IReadOnlyCollection<Guid> ids)
    {
        if (!Enum.IsDefined(mode)) return false;
        if (mode == TheoryCorrectionMode.FullCredit) return ids.Count == 0;
        if (ids.Distinct().Count() != ids.Count || ids.Any(id => question.Options.All(option => option.Id != id))) return false;
        return question.Kind == TheoryQuestionKind.MultipleChoice ? ids.Count >= 2 : ids.Count == 1;
    }

    public static void Apply(TheoryPaperItem item, TheoryGradingOverride correction)
    {
        if (item.RevisionId != correction.RevisionId) throw new InvalidOperationException("Grading override does not belong to the issued version.");
        item.Result = correction.Mode == TheoryCorrectionMode.FullCredit ? TheoryAnswerResult.FullCredit
            : !item.Options.Any(option => option.Selected) ? TheoryAnswerResult.Unanswered
            : item.Options.Where(option => option.Selected).Select(option => option.OptionId).ToHashSet()
                .SetEquals(correction.Options.Select(option => option.OptionId)) ? TheoryAnswerResult.Correct : TheoryAnswerResult.Wrong;
        item.AppliedGradingRevision = correction.TargetRevision;
    }
}
