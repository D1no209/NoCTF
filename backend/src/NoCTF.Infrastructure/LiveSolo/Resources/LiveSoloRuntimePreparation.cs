using Microsoft.EntityFrameworkCore;
using NoCTF.Application.LiveSolo.Resources;
using NoCTF.Application.LiveSolo.Rounds;
using NoCTF.Application.Runtime.Instances;
using NoCTF.Domain.LiveSolo;
using NoCTF.Domain.Runtime;
using NoCTF.Infrastructure.Persistence;

namespace NoCTF.Infrastructure.LiveSolo.Resources;

public sealed class LiveSoloRuntimePreparation(NoCtfDbContext db, IScopedRuntimeControl runtimes, NoCTF.Application.Messaging.IPostCommitMessagePublisher? messages = null) : ILiveSoloRuntimePreparation
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
            if (slot.TeamId is not Guid team) return await RevokeReadinessAsync(questionId, LiveSoloFailure.NotReady, ct);
            ScopedRuntimeResult result;
            try
            {
                result = await runtimes.EnsureAsync(new(scope.Match.CompetitionId, scope.Question.CompetitionChallengeId, questionId, team, now), ct);
            }
            catch (Exception) when (!ct.IsCancellationRequested)
            {
                return await RevokeReadinessAsync(questionId, LiveSoloFailure.DependencyUnavailable, ct);
            }
            if (LiveSoloPreparationPolicy.Failure(result) is { } failure) return await RevokeReadinessAsync(questionId, failure, ct);
            if (result.RuntimeInstanceId is Guid runtime) bindings.Add(new() { RoundQuestionId = questionId, Side = slot.Side, RuntimeInstanceId = runtime });
            if (result.RuntimeInstanceId is not null && result.State != RuntimeState.Running) ready = false;
        }
        await using var transaction = await AggregateCompatibleTransaction.BeginAsync(db, System.Data.IsolationLevel.Serializable, ct);
        var currentRound = await db.LiveSoloRounds.SingleAsync(x => x.Id == scope.Round.Id, ct);
        if (currentRound.State is not (LiveSoloRoundState.Preparing or LiveSoloRoundState.Countdown or LiveSoloRoundState.Running)
            || !await db.LiveSoloMatches.AnyAsync(x => x.Id == scope.Match.Id && x.CurrentRoundId == currentRound.Id && x.PendingCorrectionId == null, ct)) return LiveSoloFailure.AlreadyEnded;
        var question = await db.LiveSoloRoundQuestions.Include(x => x.Runtimes).SingleAsync(x => x.Id == questionId, ct);
        var changed = question.Readiness != (ready ? LiveSoloQuestionReadiness.Ready : LiveSoloQuestionReadiness.Preparing)
            || question.PreparationFailure != LiveSoloResourceFailure.None || question.EnvironmentRequired != (bindings.Count > 0);
        foreach (var binding in bindings)
        {
            var existing = question.Runtimes.SingleOrDefault(x => x.Side == binding.Side);
            if (existing is null) { question.Runtimes.Add(binding); db.Set<LiveSoloRuntimeBinding>().Add(binding); changed = true; }
            else if (existing.RuntimeInstanceId != binding.RuntimeInstanceId) { existing.RuntimeInstanceId = binding.RuntimeInstanceId; changed = true; }
        }
        question.PreparationFailure = LiveSoloResourceFailure.None;
        question.EnvironmentRequired = bindings.Count > 0;
        question.Readiness = ready ? LiveSoloQuestionReadiness.Ready : LiveSoloQuestionReadiness.Preparing;
        if (changed) currentRound.ConcurrencyStamp = Guid.NewGuid();
        await db.SaveChangesAsync(ct); await transaction.CommitAsync(ct);
        if (changed && messages is not null)
        {
            await messages.PublishAsync(new NoCTF.Application.LiveSolo.Realtime.LiveSoloMatchChanged(scope.Match.CompetitionId, scope.Match.Id, now));
            await messages.FlushCommittedMessagesAsync();
        }
        return ready ? null : LiveSoloFailure.NotReady;
    }

    private async Task<LiveSoloFailure> RevokeReadinessAsync(Guid questionId, LiveSoloFailure failure, CancellationToken ct)
    {
        await using var transaction = await AggregateCompatibleTransaction.BeginAsync(db, System.Data.IsolationLevel.Serializable, ct);
        var question = await db.LiveSoloRoundQuestions.SingleAsync(x => x.Id == questionId, ct);
        var round = await db.LiveSoloRounds.SingleAsync(x => x.Id == question.RoundId, ct);
        if (round.State is not (LiveSoloRoundState.Preparing or LiveSoloRoundState.Countdown or LiveSoloRoundState.Running)) return LiveSoloFailure.AlreadyEnded;
        question.Readiness = LiveSoloQuestionReadiness.Failed;
        question.PreparationFailure = LiveSoloPreparationPolicy.Persisted(failure);
        question.ConcurrencyStamp = Guid.NewGuid(); round.ConcurrencyStamp = Guid.NewGuid();
        await db.SaveChangesAsync(ct); await transaction.CommitAsync(ct);
        if (messages is not null)
        {
            var match = await db.LiveSoloMatches.AsNoTracking().SingleAsync(x => x.Id == round.MatchId, ct);
            await messages.PublishAsync(new NoCTF.Application.LiveSolo.Realtime.LiveSoloMatchChanged(match.CompetitionId, match.Id, TimeProvider.System.GetUtcNow()));
            await messages.FlushCommittedMessagesAsync();
        }
        return failure;
    }

    public async Task StopRoundAsync(Guid roundId, DateTimeOffset now, CancellationToken ct)
    {
        foreach (var id in await db.LiveSoloRoundQuestions.Where(x => x.RoundId == roundId).Select(x => x.Id).ToArrayAsync(ct))
            await runtimes.StopExecutionAsync(id, now, ct);
    }
}
