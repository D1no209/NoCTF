using NoCTF.Domain.Questionnaires.Activities;
using NoCTF.Domain.Questionnaires.Sessions;
using NoCTF.Domain.Challenges.Capabilities;

namespace NoCTF.Application.Questionnaires.Sessions;

public sealed record QuestionnaireSessionScore(Guid SessionId, int Attempt, long Score, DateTimeOffset SubmittedAt);
public sealed record QuestionnaireEffectiveScore(long Points, DateTimeOffset? AchievedAt, bool HasSubmission);

public static class QuestionnaireSessionGrading
{
    public static long Points(QuestionnaireSession session)
    {
        if (session.State != QuestionnaireSessionState.Submitted) return 0;
        if (session.Items.Any(item => item.Result is null))
            throw new InvalidOperationException("A submitted session must be graded in full.");
        return session.Items.Aggregate(0L, (sum, item) => checked(sum
            + (item.Result is QuestionnaireAnswerResult.Correct or QuestionnaireAnswerResult.FullCredit ? item.Points : 0)));
    }

    public static QuestionnaireEffectiveScore Aggregate(QuestionnaireScoringMode mode, IReadOnlyList<QuestionnaireSessionScore> sessions)
    {
        if (!Enum.IsDefined(mode)) throw new ArgumentOutOfRangeException(nameof(mode));
        if (sessions.Count == 0) return new(0, null, false);
        if (sessions.Any(session => session.Score < 0 || session.Attempt <= 0)
            || sessions.Select(session => session.Attempt).Distinct().Count() != sessions.Count)
            throw new ArgumentException("Only distinct valid submitted attempts may be aggregated.", nameof(sessions));
        var selected = mode switch
        {
            QuestionnaireScoringMode.Highest => sessions.OrderByDescending(session => session.Score)
                .ThenBy(session => session.SubmittedAt).ThenBy(session => session.Attempt).First(),
            QuestionnaireScoringMode.Latest => sessions.MaxBy(session => session.Attempt)!,
            QuestionnaireScoringMode.Cumulative => sessions.MaxBy(session => session.SubmittedAt)!,
            _ => throw new ArgumentOutOfRangeException(nameof(mode))
        };
        return new(mode == QuestionnaireScoringMode.Cumulative
            ? sessions.Aggregate(0L, (sum, session) => checked(sum + session.Score)) : selected.Score,
            selected.SubmittedAt, true);
    }
}
