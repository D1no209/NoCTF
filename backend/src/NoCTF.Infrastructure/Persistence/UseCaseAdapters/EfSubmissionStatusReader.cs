using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Submissions.Ports;
using NoCTF.Domain.Submissions;

namespace NoCTF.Infrastructure.Persistence.UseCaseAdapters;

public sealed class EfSubmissionStatusReader(NoCtfDbContext db) : ISubmissionStatusReader
{
    public async Task<SubmissionStatusView?> FindAsync(Guid competitionId, Guid submissionId, Guid userId, CancellationToken ct)
    {
        var canRead = await db.CompetitionCollaborators.AsNoTracking().AnyAsync(x => x.CompetitionId == competitionId && x.UserId == userId, ct)
            || await db.TeamMembers.AsNoTracking().AnyAsync(x => x.CompetitionId == competitionId && x.UserId == userId, ct);
        if (!canRead) return null;
        var item = await db.Submissions.AsNoTracking().Include(x => x.ScoringEvent)
            .SingleOrDefaultAsync(x => x.Id == submissionId && x.CompetitionId == competitionId, ct);
        if (item?.TeamId is not Guid teamId || item.ChallengeId is not Guid challengeId) return null;
        return new(item.Id, item.CompetitionId, teamId, challengeId, item.Kind, item.ScoringEvent?.Result, item.ReceivedAt,
            item.ScoringEvent?.ProcessedAt, item.ScoringEvent?.FailureCode, item.ScoringEvent?.EvaluatorVersion);
    }

}
