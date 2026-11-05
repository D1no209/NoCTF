using System.Data;
using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Commands.Idempotency;
using NoCTF.Application.LiveSolo.Resources;
using NoCTF.Application.LiveSolo.Rounds;
using NoCTF.Application.Messaging;
using NoCTF.Application.Runtime.Access;
using NoCTF.Application.Runtime.Instances;
using NoCTF.Application.Runtime.Provisioning;
using NoCTF.Domain.Challenges;
using NoCTF.Domain.Commands;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.LiveSolo;
using NoCTF.Domain.Runtime;
using NoCTF.GameModes.Challenges.Capabilities;
using NoCTF.Infrastructure.Persistence;

namespace NoCTF.Infrastructure.LiveSolo.Resources;

public sealed class LiveSoloFixTargetStore(NoCtfDbContext db, IExecutionScopeAccess access, IRuntimePlacementPolicy placement,
    IPostCommitMessagePublisher messages, TimeProvider clock, IRequestReplay? replay = null) : ILiveSoloFixTargetStore
{
    private sealed record Scope(Guid TeamId, LiveSoloSide Side, LiveSoloRound Round, LiveSoloRoundQuestion Question,
        Competition Competition, CompetitionChallenge Challenge, Challenge Template);

    private async Task<Scope?> ScopeAsync(LiveSoloResourceRequest request, ExecutionScopeOperation operation, CancellationToken ct)
    {
        var teamId = await db.Teams.AsNoTracking().Where(team => team.CompetitionId == request.CompetitionId
                && team.Members.Any(member => member.UserId == request.ActorId)).Select(team => (Guid?)team.Id).SingleOrDefaultAsync(ct);
        var question = await db.LiveSoloRoundQuestions.SingleOrDefaultAsync(value => value.Id == request.QuestionId && value.RoundId == request.RoundId, ct);
        if (teamId is null || question is null) return null;
        var round = await db.LiveSoloRounds.SingleOrDefaultAsync(value => value.Id == request.RoundId && value.MatchId == request.MatchId, ct);
        var match = await db.LiveSoloMatches.AsNoTracking().Include(value => value.Slots).SingleOrDefaultAsync(value => value.Id == request.MatchId
            && value.CompetitionId == request.CompetitionId && value.CurrentRoundId == request.RoundId && value.PendingCorrectionId == null && value.SupersededAt == null, ct);
        if (round is null || match is null || !await access.CanAccessAsync(new(question.Id, request.CompetitionId,
                question.CompetitionChallengeId, teamId, request.ActorId, operation, clock.GetUtcNow()), ct)) return null;
        var challenge = await db.CompetitionChallenges.AsNoTracking().AsSplitQuery().SingleAsync(value => value.Id == question.CompetitionChallengeId, ct);
        if (challenge.Rules is not LiveSoloCompetitionChallengeRules { CompletionKind: LiveSoloCompletionKind.FixAndCheck }) return null;
        var competition = await db.Competitions.AsNoTracking().AsSplitQuery().SingleAsync(value => value.Id == request.CompetitionId, ct);
        var template = await db.Challenges.AsNoTracking().AsSplitQuery().SingleAsync(value => value.Id == challenge.ChallengeId, ct);
        var side = match.Slots.Single(value => value.TeamId == teamId).Side;
        return new(teamId.Value, side, round, question, competition, challenge, template);
    }

    public async Task<LiveSoloFixTargetResult> ReadAsync(LiveSoloResourceRequest request, CancellationToken ct)
    {
        var scope = await ScopeAsync(request, ExecutionScopeOperation.Read, ct);
        if (scope is null) return new(Failure: LiveSoloFailure.NotFound);
        var runtime = await db.Set<LiveSoloFixTargetOwnership>().AsNoTracking().Where(binding => binding.RoundQuestionId == scope.Question.Id && binding.Side == scope.Side)
            .Join(db.RuntimeInstances.AsNoTracking(), binding => binding.RuntimeInstanceId, instance => instance.Id, (_, instance) => instance)
            .Select(instance => new { instance.Id, instance.State, instance.GameplayFactId, instance.CreatedAt,
                Sequence = db.LiveSoloSubmissions.Where(value => value.GameplayFactId == instance.GameplayFactId).Select(value => (long?)value.AdmissionSequence).SingleOrDefault() })
            .OrderByDescending(instance => instance.GameplayFactId == null
                && (instance.State == RuntimeState.Queued || instance.State == RuntimeState.Provisioning || instance.State == RuntimeState.Running))
            .ThenByDescending(instance => instance.CreatedAt).ThenByDescending(instance => instance.Sequence).ThenByDescending(instance => instance.Id)
            .FirstOrDefaultAsync(ct);
        return runtime is null ? new() : new(runtime.Id, runtime.State, runtime.GameplayFactId, AdmissionSequence: runtime.Sequence);
    }

    public async Task<LiveSoloFixTargetResult> RequestAsync(LiveSoloResourceRequest request, CancellationToken ct)
    {
        for (var attempt = 0; ; attempt++)
        {
            var committed = false;
            try
            {
                await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
                var scope = await ScopeAsync(request, ExecutionScopeOperation.Start, ct);
                if (scope is null) return new(Failure: LiveSoloFailure.SubmissionActionUnsupported);
                var prior = replay is null ? null : await replay.FindAsync<RuntimeCommandReceipt>(
                    new(request.ActorId, ReplayOperation.RuntimeMutation, request.CompetitionId, request.QuestionId),
                    new ScopedRuntimeReplayFingerprint(RuntimeAction.Start, scope.TeamId, null), ct);
                if (prior is not null)
                {
                    var old = await db.RuntimeInstances.AsNoTracking().SingleAsync(value => value.Id == prior.RuntimeInstanceId
                        && value.CompetitionId == request.CompetitionId && value.CompetitionChallengeId == scope.Challenge.Id && value.TeamId == scope.TeamId, ct);
                    return new(old.Id, old.State, old.GameplayFactId);
                }
                var configuration = FixSubmissionConfigurationResolver.Resolve(GameMode.LiveSolo, scope.Competition.ModeConfiguration!,
                    scope.Challenge.Rules!, scope.Template.Definition!);
                if (configuration?.Runtime.Definition is not ContainerRuntimeDefinition)
                    return new(Failure: LiveSoloFailure.UnsupportedEnvironment);
                var current = await db.Set<LiveSoloFixTargetOwnership>().Where(value => value.RoundQuestionId == scope.Question.Id && value.Side == scope.Side)
                    .Join(db.RuntimeInstances, value => value.RuntimeInstanceId, instance => instance.Id, (_, instance) => instance)
                    .Where(instance => instance.GameplayFactId == null
                        && (instance.State == RuntimeState.Queued || instance.State == RuntimeState.Provisioning || instance.State == RuntimeState.Running))
                    .OrderByDescending(instance => instance.CreatedAt).ThenByDescending(instance => instance.Id).FirstOrDefaultAsync(ct);
                if (current is { GameplayFactId: null, State: RuntimeState.Queued or RuntimeState.Provisioning or RuntimeState.Running })
                {
                    replay?.Store(new RuntimeCommandReceipt(current.Id));
                    await db.SaveChangesAsync(ct); await transaction.CommitAsync(ct); committed = true;
                    return new(current.Id, current.State);
                }
                var count = await db.RuntimeInstances.CountAsync(value => value.CompetitionId == request.CompetitionId && value.TeamId == scope.TeamId
                    && (value.State == RuntimeState.Queued || value.State == RuntimeState.Provisioning || value.State == RuntimeState.Running || value.State == RuntimeState.Stopping), ct);
                if (scope.Competition.MaxConcurrentRuntimeInstancesPerTeam > 0 && count >= scope.Competition.MaxConcurrentRuntimeInstancesPerTeam)
                    return new(Failure: LiveSoloFailure.ResourceCapacityExceeded);
                var now = clock.GetUtcNow();
                if (!await access.CanAccessAsync(new(scope.Question.Id, request.CompetitionId, scope.Challenge.Id, scope.TeamId,
                        request.ActorId, ExecutionScopeOperation.Start, now), ct)) return new(Failure: LiveSoloFailure.DeadlinePassed);
                var id = Guid.CreateVersion7(now);
                var runtime = new ScopedFixTargetRuntimeInstance { Id = id, CompetitionId = request.CompetitionId,
                    CompetitionChallengeId = scope.Challenge.Id, TeamId = scope.TeamId, RuntimeKind = RuntimeKind.Container,
                    RuntimeProvider = placement.Resolve(RuntimeKind.Container).Provider, AccessMode = RuntimeAccessMode.Direct,
                    State = RuntimeState.Queued, CreatedAt = now };
                db.RuntimeInstances.Add(runtime);
                db.Add(new LiveSoloFixTargetOwnership { RoundQuestionId = scope.Question.Id, Side = scope.Side, RuntimeInstanceId = id });
                scope.Round.ConcurrencyStamp = Guid.NewGuid();
                replay?.Store(new RuntimeCommandReceipt(id));
                await db.SaveChangesAsync(ct); await transaction.CommitAsync(ct); committed = true;
                await messages.PublishAsync(new DispatchRuntime(id));
                await messages.PublishAsync(new NoCTF.Application.LiveSolo.Realtime.LiveSoloMatchChanged(request.CompetitionId, request.MatchId, now));
                await messages.FlushCommittedMessagesAsync();
                return new(id, runtime.State);
            }
            catch (Exception exception) when (!committed && attempt < 2 && (TransactionFailureClassifier.IsRetryable(exception) || exception is DbUpdateException))
            { db.ChangeTracker.Clear(); messages.DiscardPendingMessages(); }
            catch (Exception exception) when (!committed && (TransactionFailureClassifier.IsRetryable(exception) || exception is DbUpdateException))
            { db.ChangeTracker.Clear(); messages.DiscardPendingMessages(); return new(Failure: LiveSoloFailure.Conflict); }
        }
    }
}
