using Marten;
using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Submissions.Events;
using NoCTF.Application.Submissions.Ports;
using EF = Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions;

namespace NoCTF.Infrastructure.Eventing.SubmissionStreams;

public sealed class MartenSubmissionStatusReader(IQuerySession session, Persistence.NoCtfDbContext db)
    : ISubmissionStatusReader
{
    public async Task<SubmissionStatusView?> FindAsync(
        Guid competitionId,
        Guid submissionId,
        Guid userId,
        CancellationToken cancellationToken)
    {
        var isCollaborator = await EF.AnyAsync(
            db.CompetitionCollaborators,
            item => item.CompetitionId == competitionId && item.UserId == userId,
            cancellationToken);
        var teamIds = await EF.ToListAsync(db.TeamMembers
            .Where(item => item.CompetitionId == competitionId && item.UserId == userId)
            .Select(item => item.TeamId), cancellationToken);

        var events = await session.Events.FetchStreamAsync(StreamIds.Submission(competitionId), token: cancellationToken);
        var flag = events.Select(item => item.Data).OfType<FlagSubmissionReceived>()
            .SingleOrDefault(item => item.SubmissionId == submissionId);
        var fix = events.Select(item => item.Data).OfType<FixSubmissionReceived>()
            .SingleOrDefault(item => item.SubmissionId == submissionId);
        var teamId = flag?.TeamId ?? fix?.TeamId;
        if (teamId is null || (!isCollaborator && !teamIds.Contains(teamId.Value)))
            return null;

        var outcome = events.Select(item => item.Data).OfType<FlagSubmissionEvaluated>()
            .Where(item => item.SubmissionId == submissionId)
            .Select(item => (item.Outcome, item.EvaluatedAt, item.ErrorCode))
            .Concat(events.Select(item => item.Data).OfType<FixSubmissionEvaluated>()
                .Where(item => item.SubmissionId == submissionId)
                .Select(item => (item.Outcome, item.EvaluatedAt, item.ErrorCode)))
            .LastOrDefault();

        return flag is not null
            ? new(submissionId, competitionId, flag.TeamId, flag.ChallengeId, SubmissionKind.Flag,
                outcome.Outcome, flag.ReceivedAt, outcome.EvaluatedAt == default ? null : outcome.EvaluatedAt, outcome.ErrorCode)
            : new(submissionId, competitionId, fix!.TeamId, fix.ChallengeId, SubmissionKind.Fix,
                outcome.Outcome, fix.ReceivedAt, outcome.EvaluatedAt == default ? null : outcome.EvaluatedAt, outcome.ErrorCode);
    }
}
