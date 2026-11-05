using NoCTF.Domain.Questionnaires.Sessions;
using NoCTF.Domain.Challenges.Capabilities;

namespace NoCTF.Application.Questionnaires.Sessions;

public sealed record QuestionnaireParticipantOption(Guid Id, int Order, string Content, bool? TruthValue, bool Selected);
public sealed record QuestionnaireParticipantQuestion(Guid Id, Guid AnswerRevision, int Order, QuestionnaireQuestionKind Kind,
    string Body, int Points, Guid? AnsweredById, DateTimeOffset? AnsweredAt, IReadOnlyList<QuestionnaireParticipantOption> Options)
{
    public string? AnsweredByName { get; init; }
}
public sealed record QuestionnaireParticipantSession(Guid Id, Guid AnswerRevision, int Attempt, QuestionnaireSessionState State,
    DateTimeOffset OpenedAt, DateTimeOffset? SubmittedAt, QuestionnaireSubmissionKind? SubmissionKind,
    long MaximumPoints, long? Score, IReadOnlyList<QuestionnaireParticipantQuestion> Questions);

/// <summary>Explicit participant projection: neither entities nor correct-answer fields cross this boundary.</summary>
public static class ReadQuestionnaireSession
{
    public static QuestionnaireParticipantSession Project(QuestionnaireSession paper, IReadOnlyDictionary<Guid, QuestionnaireQuestion> versions, IReadOnlyDictionary<Guid, string>? actors = null,
        bool includeIssuedContent = false)
    {
        var questions = paper.State == QuestionnaireSessionState.Active || includeIssuedContent
            ? paper.Items.OrderBy(item => item.Order).Select(item =>
            {
                var version = versions[item.RevisionId];
                var options = version.Options.ToDictionary(option => option.Id);
                return new QuestionnaireParticipantQuestion(item.Id, item.ConcurrencyStamp, item.Order, version.Kind,
                    version.Body, item.Points, item.AnsweredById, item.AnsweredAt,
                    item.Options.OrderBy(option => option.Order).Select(option => new QuestionnaireParticipantOption(
                        option.OptionId, option.Order, options[option.OptionId].Content,
                        options[option.OptionId].TruthValue, option.Selected)).ToArray())
                { AnsweredByName = item.AnsweredById is { } actorId && actors is not null ? actors.GetValueOrDefault(actorId) : null };
            }).ToArray() : [];
        return new(paper.Id, paper.ConcurrencyStamp, paper.Attempt, paper.State, paper.OpenedAt,
            paper.SubmittedAt, paper.SubmissionKind, paper.Items.Sum(item => (long)item.Points),
            paper.State == QuestionnaireSessionState.Submitted ? QuestionnaireSessionGrading.Points(paper) : null, questions);
    }
}
