using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Submissions.Events;
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
        var outcome = item.ScoringEvent is null ? SubmissionOutcome.Pending : ToOutcome(item.ScoringEvent.Result);
        return new(item.Id, item.CompetitionId, teamId, challengeId, ToKind(item.Kind), outcome, item.ReceivedAt,
            item.ScoringEvent?.ProcessedAt, item.ScoringEvent is null ? null : ToError(item.ScoringEvent.FailureCode));
    }

    private static SubmissionOutcome ToOutcome(ScoringResult value) => value switch
    {
        ScoringResult.Correct => SubmissionOutcome.Correct,
        ScoringResult.Wrong => SubmissionOutcome.Wrong,
        ScoringResult.Duplicate => SubmissionOutcome.Duplicate,
        ScoringResult.AttemptsExhausted => SubmissionOutcome.AttemptsExhausted,
        ScoringResult.PlatformFailed => SubmissionOutcome.PlatformFailed,
        _ => SubmissionOutcome.Rejected
    };
    private static NoCTF.Application.Submissions.Events.SubmissionKind ToKind(NoCTF.Domain.Submissions.SubmissionKind value) => value == NoCTF.Domain.Submissions.SubmissionKind.Fix ? NoCTF.Application.Submissions.Events.SubmissionKind.Fix : NoCTF.Application.Submissions.Events.SubmissionKind.Flag;
    private static SubmissionErrorCode? ToError(ScoringFailureCode? value) => value is null ? null : Enum.TryParse<SubmissionErrorCode>(value.Value.ToString(), out var error) ? error : null;
}
