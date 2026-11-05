using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Challenges.Configuration;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Challenges;
using NoCTF.GameModes.Registration;
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
        if (template is null) return;
        var used = await db.LiveSoloRoundQuestions.Where(x => db.CompetitionChallenges.Any(c => c.Id == x.CompetitionChallengeId && c.ChallengeId == id))
            .Join(db.LiveSoloRounds, x => x.RoundId, x => x.Id, (question, round) => new { Question = question, Round = round })
            .Where(x => x.Round.State == LiveSoloRoundState.Preparing || x.Round.State == LiveSoloRoundState.Countdown
                || x.Round.State == LiveSoloRoundState.Running || x.Round.State == LiveSoloRoundState.ConfirmingResult)
            .Select(x => x.Question.CompetitionChallengeId).Distinct().ToArrayAsync(ct);
        var rules = await db.Set<LiveSoloCompetitionChallengeRules>().Where(rule => used.Contains(rule.CompetitionChallengeId)).ToArrayAsync(ct);
        if (rules.Any(rule => scope.RuntimeChanged || scope.AffectedCapabilities is null
                || GameModeChallengeConfigurationCatalog.RequiredCapabilities(rule).Intersect(scope.AffectedCapabilities).Any()))
            throw new ChallengeMaterialMutationException(ChallengeMaterialMutationFailure.ActiveExecutionScope);
        // Preparation reads this row under serializable isolation, fencing a simultaneous material edit.
        if (db.Database.CurrentTransaction is not null) template.ConcurrencyStamp = Guid.NewGuid();
    }
}
