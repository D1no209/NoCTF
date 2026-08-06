using Microsoft.EntityFrameworkCore;
using NoCTF.Domain.Submissions;
using NoCTF.Infrastructure.Persistence;

namespace NoCTF.Infrastructure.Submissions.Intake;

internal static class SubmissionAttemptLock
{
    public static ValueTask<IAsyncDisposable> AcquireAsync(
        NoCtfDbContext db,
        Guid teamId,
        Guid competitionChallengeId,
        SubmissionKind kind,
        CancellationToken cancellationToken) =>
        CriticalSectionCoordinator.AcquireAsync(
            db,
            $"submission-attempt:{teamId:N}:{competitionChallengeId:N}:{(short)kind}",
            token => db.Teams.Where(team => team.Id == teamId)
                .ExecuteUpdateAsync(setters => setters.SetProperty(
                    team => team.CriticalSectionVersion,
                    team => team.CriticalSectionVersion + 1), token),
            cancellationToken);
}
