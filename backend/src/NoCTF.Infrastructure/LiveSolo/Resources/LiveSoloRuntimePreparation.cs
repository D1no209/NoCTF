using Microsoft.EntityFrameworkCore;
using NoCTF.Application.LiveSolo.Resources;
using NoCTF.Application.LiveSolo.Rounds;
using NoCTF.Application.Runtime.Instances;
using NoCTF.Domain.LiveSolo;
using NoCTF.Domain.Runtime;
using NoCTF.Infrastructure.Persistence;

namespace NoCTF.Infrastructure.LiveSolo.Resources;

public sealed class LiveSoloRuntimePreparation(NoCtfDbContext db, IScopedRuntimeControl runtimes) : ILiveSoloRuntimePreparation
{
    public async Task<LiveSoloFailure?> PrepareAsync(Guid questionId, DateTimeOffset now, CancellationToken ct)
    {
        var scope = await db.LiveSoloRoundQuestions.AsNoTracking().Where(x => x.Id == questionId)
            .Join(db.LiveSoloRounds.AsNoTracking(), x => x.RoundId, x => x.Id, (question, round) => new { Question = question, Round = round })
            .Join(db.LiveSoloMatches.AsNoTracking().Include(x => x.Slots), x => x.Round.MatchId, x => x.Id, (x, match) => new { x.Question, x.Round, Match = match })
            .SingleOrDefaultAsync(ct);
        if (scope is null || scope.Round.State is not (LiveSoloRoundState.Preparing or LiveSoloRoundState.Countdown or LiveSoloRoundState.Running)) return LiveSoloFailure.NotFound;
        var bindings = new List<LiveSoloRuntimeBinding>();
        var ready = true;
        foreach (var slot in scope.Match.Slots)
        {
            if (slot.TeamId is not Guid team) return LiveSoloFailure.NotReady;
            var result = await runtimes.EnsureAsync(new(scope.Match.CompetitionId, scope.Question.CompetitionChallengeId, questionId, team, now), ct);
            if (!result.IsolationAvailable) return LiveSoloFailure.IsolationUnavailable;
            if (result.Failure is not null) return LiveSoloFailure.DependencyUnavailable;
            if (result.RuntimeInstanceId is Guid runtime) bindings.Add(new() { RoundQuestionId = questionId, Side = slot.Side, RuntimeInstanceId = runtime });
            if (result.RuntimeInstanceId is not null && result.State != RuntimeState.Running) ready = false;
        }
        var question = await db.LiveSoloRoundQuestions.Include(x => x.Runtimes).SingleAsync(x => x.Id == questionId, ct);
        foreach (var binding in bindings)
        {
            var existing = question.Runtimes.SingleOrDefault(x => x.Side == binding.Side);
            if (existing is null) { question.Runtimes.Add(binding); db.Set<LiveSoloRuntimeBinding>().Add(binding); }
            else existing.RuntimeInstanceId = binding.RuntimeInstanceId;
        }
        question.Readiness = ready ? LiveSoloQuestionReadiness.Ready : LiveSoloQuestionReadiness.Preparing;
        await db.SaveChangesAsync(ct);
        return ready ? null : LiveSoloFailure.NotReady;
    }

    public async Task StopRoundAsync(Guid roundId, DateTimeOffset now, CancellationToken ct)
    {
        foreach (var id in await db.LiveSoloRoundQuestions.Where(x => x.RoundId == roundId).Select(x => x.Id).ToArrayAsync(ct))
            await runtimes.StopExecutionAsync(id, now, ct);
    }
}
