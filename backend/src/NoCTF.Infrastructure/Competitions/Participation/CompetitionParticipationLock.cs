using Microsoft.EntityFrameworkCore;
using NoCTF.Infrastructure.Persistence;

namespace NoCTF.Infrastructure.Competitions.Participation;

/// <summary>Admission lock order: competition SHARE, then team UPDATE, then challenge/runtime/file.</summary>
public static class CompetitionParticipationLock
{
    public static async Task AcquireAsync(NoCtfDbContext db, Guid competitionId, CancellationToken ct)
    {
        if (!db.Database.IsRelational()) return;
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(ct);
        budget.CancelAfter(TimeSpan.FromSeconds(2));
        try
        {
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM competitions WHERE id = {competitionId} FOR SHARE", budget.Token);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        { throw new FeatureCriticalSectionTimeoutException("competition-admission"); }
    }
    public static async Task AcquireForChallengeAsync(NoCtfDbContext db, Guid challengeId, CancellationToken ct)
    {
        var id = await db.CompetitionChallenges.AsNoTracking().Where(challenge => challenge.Id == challengeId)
            .Select(challenge => (Guid?)challenge.CompetitionId).SingleOrDefaultAsync(ct);
        if (id is Guid competitionId) await AcquireAsync(db, competitionId, ct);
    }
}
