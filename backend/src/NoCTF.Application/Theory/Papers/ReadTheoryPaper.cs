using NoCTF.Domain.Theory.Activities;
using NoCTF.Domain.Theory.Papers;
using NoCTF.Domain.Theory.Questions;

namespace NoCTF.Application.Theory.Papers;

public sealed record TheoryParticipantOption(Guid Id, int Order, string Content, TheoryTruthValue? TruthValue, bool Selected);
public sealed record TheoryParticipantQuestion(Guid Id, Guid AnswerRevision, int Order, TheoryQuestionKind Kind,
    string Body, int Points, Guid? AnsweredById, DateTimeOffset? AnsweredAt, IReadOnlyList<TheoryParticipantOption> Options);
public sealed record TheoryParticipantPaper(Guid Id, Guid AnswerRevision, int Attempt, TheoryPaperState State,
    DateTimeOffset OpenedAt, DateTimeOffset? SubmittedAt, TheorySubmissionKind? SubmissionKind,
    long MaximumPoints, long? Score, IReadOnlyList<TheoryParticipantQuestion> Questions);

/// <summary>Explicit participant projection: neither entities nor correct-answer fields cross this boundary.</summary>
public static class ReadTheoryPaper
{
    public static TheoryParticipantPaper Project(TheoryPaper paper, IReadOnlyDictionary<Guid, TheoryQuestionRevision> versions)
    {
        var questions = paper.State == TheoryPaperState.Active
            ? paper.Items.OrderBy(item => item.Order).Select(item =>
            {
                var version = versions[item.RevisionId];
                var options = version.Options.ToDictionary(option => option.Id);
                return new TheoryParticipantQuestion(item.Id, item.ConcurrencyStamp, item.Order, version.Kind,
                    version.Body, item.Points, item.AnsweredById, item.AnsweredAt,
                    item.Options.OrderBy(option => option.Order).Select(option => new TheoryParticipantOption(
                        option.OptionId, option.Order, options[option.OptionId].Content,
                        options[option.OptionId].TruthValue, option.Selected)).ToArray());
            }).ToArray() : [];
        return new(paper.Id, paper.ConcurrencyStamp, paper.Attempt, paper.State, paper.OpenedAt,
            paper.SubmittedAt, paper.SubmissionKind, paper.Items.Sum(item => (long)item.Points),
            paper.State == TheoryPaperState.Submitted ? TheoryPaperGrading.Points(paper) : null, questions);
    }
}
