using Microsoft.EntityFrameworkCore;
using NoCTF.Domain.Submissions;
using NoCTF.Infrastructure.Persistence;

namespace NoCTF.Infrastructure.Submissions.Intake;

internal static class SubmissionAttemptLock
{
    public static Task AcquireAsync(
        NoCtfDbContext db,
        Guid teamId,
        Guid competitionChallengeId,
        SubmissionKind kind,
        CancellationToken cancellationToken) =>
        db.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock(hashtextextended({"s" + teamId.ToString("N") + competitionChallengeId.ToString("N") + ((short)kind).ToString()}, 0))",
            cancellationToken);
}
