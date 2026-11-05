using NoCTF.Application.Common;
using NoCTF.Domain.Challenges.Capabilities;

namespace NoCTF.Application.Challenges.Capabilities;

public sealed record QuestionnaireAnswer(Guid QuestionId, IReadOnlyList<Guid> SelectedOptionIds);
public sealed record QuestionnaireInput(Guid ExecutionId, QuestionnaireRevision Revision,
    IReadOnlyList<Guid> AssignedQuestionIds, IReadOnlyList<QuestionnaireAnswer> Answers, DateTimeOffset ObservedAt,
    CapabilityExecutionScope Scope = CapabilityExecutionScope.GameplayFact);

public sealed class EvaluateQuestionnaire
{
    public OperationResult<QuestionnaireEvaluation, CapabilityExecutionFailure> Execute(QuestionnaireInput input)
    {
        var assigned = input.AssignedQuestionIds.ToHashSet();
        if (input.ExecutionId == Guid.Empty || !Enum.IsDefined(input.Scope) || input.Revision.Id == Guid.Empty || assigned.Count == 0
            || assigned.Count != input.AssignedQuestionIds.Count
            || input.Answers.Select(answer => answer.QuestionId).Distinct().Count() != input.Answers.Count
            || input.Answers.Any(answer => !assigned.Contains(answer.QuestionId))) return Invalid();
        var questions = input.Revision.Questions.ToDictionary(question => question.Id);
        if (assigned.Any(id => !questions.ContainsKey(id))) return Invalid();
        var answers = input.Answers.ToDictionary(answer => answer.QuestionId);
        var results = new List<QuestionnaireQuestionEvidence>();
        foreach (var id in input.AssignedQuestionIds)
        {
            var question = questions[id];
            var selected = answers.TryGetValue(id, out var answer) ? answer.SelectedOptionIds : [];
            var unique = selected.ToHashSet();
            if (unique.Count != selected.Count || unique.Any(optionId => question.Options.All(option => option.Id != optionId))
                || question.Kind != QuestionnaireQuestionKind.MultipleChoice && unique.Count > 1) return Invalid();
            results.Add(new(id, unique.Count == 0 ? QuestionnaireAnswerResult.Unanswered
                    : unique.SetEquals(question.Options.Where(option => option.IsCorrect).Select(option => option.Id))
                        ? QuestionnaireAnswerResult.Correct : QuestionnaireAnswerResult.Wrong
            ));
        }
        return OperationResult<QuestionnaireEvaluation, CapabilityExecutionFailure>.Success(new()
        {
            ExecutionId = input.ExecutionId, Scope = input.Scope, RevisionId = input.Revision.Id, ObservedAt = input.ObservedAt, Questions = results
        });
    }

    private static OperationResult<QuestionnaireEvaluation, CapabilityExecutionFailure> Invalid() =>
        OperationResult<QuestionnaireEvaluation, CapabilityExecutionFailure>.Failure(CapabilityExecutionFailure.InvalidInput, "Answers must select distinct options from the assigned questions.");
}
