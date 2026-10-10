using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Challenges.Configuration;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.LiveSolo;
using NoCTF.Infrastructure.Persistence;

namespace NoCTF.Infrastructure.LiveSolo.Templates;

public sealed class LiveSoloMaterialMutationGate(NoCtfDbContext db) : IChallengeMaterialMutationGate
{
    public async Task RequireMutableAsync(ChallengeMaterialScope scope, CancellationToken ct)
    {
        var id = scope.ChallengeId ?? await db.CompetitionChallenges.Where(x => x.Id == scope.CompetitionChallengeId)
            .Select(x => (Guid?)x.ChallengeId).SingleOrDefaultAsync(ct);
        if (id is null) return;
        var template = await db.Challenges.SingleOrDefaultAsync(x => x.Id == id, ct);
        if (template?.Mode != GameMode.LiveSolo) return;
        if (await db.LiveSoloRoundQuestions.Where(x => db.CompetitionChallenges.Any(c => c.Id == x.CompetitionChallengeId && c.ChallengeId == id))
            .Join(db.LiveSoloRounds, x => x.RoundId, x => x.Id, (_, round) => round)
            .AnyAsync(x => x.State == LiveSoloRoundState.Preparing || x.State == LiveSoloRoundState.Countdown
                || x.State == LiveSoloRoundState.Running || x.State == LiveSoloRoundState.ConfirmingResult, ct))
            throw new ChallengeMaterialMutationException(ChallengeMaterialMutationFailure.ActiveExecutionScope);
        // Preparation reads this row under serializable isolation, fencing a simultaneous material edit.
        if (db.Database.CurrentTransaction is not null) template.ConcurrencyStamp = Guid.NewGuid();
    }
}
