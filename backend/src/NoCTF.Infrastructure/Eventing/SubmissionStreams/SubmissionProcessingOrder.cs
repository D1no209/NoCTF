using JasperFx.Events;
using NoCTF.Application.Submissions.Events;

namespace NoCTF.Infrastructure.Eventing.SubmissionStreams;

internal static class SubmissionProcessingOrder
{
    public static void EnsureEarlierSubmissionsCompleted(IReadOnlyList<IEvent> events, Guid submissionId)
    {
        var current = events.Single(item => IsSubmission(item.Data, submissionId));
        var scope = current.Data switch
        {
            FlagSubmissionReceived flag => (flag.TeamId, flag.ChallengeId),
            FixSubmissionReceived fix => (fix.TeamId, fix.ChallengeId),
            _ => throw new InvalidOperationException("Submission input was not found.")
        };
        var completed = events.Select(item => item.Data).Select(item => item switch
        {
            FlagSubmissionEvaluated flag => flag.SubmissionId,
            FixSubmissionEvaluated fix => fix.SubmissionId,
            _ => Guid.Empty
        }).Where(id => id != Guid.Empty).ToHashSet();

        var hasEarlierPending = events.Where(item => item.Version < current.Version)
            .Select(item => item.Data)
            .Any(item => item switch
            {
                FlagSubmissionReceived flag => flag.TeamId == scope.TeamId
                    && flag.ChallengeId == scope.ChallengeId && !completed.Contains(flag.SubmissionId),
                FixSubmissionReceived fix => fix.TeamId == scope.TeamId
                    && fix.ChallengeId == scope.ChallengeId && !completed.Contains(fix.SubmissionId),
                _ => false
            });
        if (hasEarlierPending)
            throw new InvalidOperationException("An earlier submission in this team/challenge partition is still pending.");
    }

    private static bool IsSubmission(object candidate, Guid submissionId) => candidate switch
    {
        FlagSubmissionReceived flag => flag.SubmissionId == submissionId,
        FixSubmissionReceived fix => fix.SubmissionId == submissionId,
        _ => false
    };
}
