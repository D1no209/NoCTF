using NoCTF.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Messaging;
using NoCTF.Application.Runtime.Instances;
using NoCTF.Application.GameplayFacts.Processing;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Runtime;
using NoCTF.Domain.Gameplay;
using NoCTF.GameModes.Awdp.Scoring;
using NoCTF.Application.Competitions.Events;
using NoCTF.Domain.Competitions.Events;
using NoCTF.Infrastructure.GameplayFacts.Awdp;
using NoCTF.GameModes.Ctf.Configuration;
using NoCTF.Domain.Challenges;
using NoCTF.Application.Scoring.Leaderboard;
using NoCTF.Infrastructure.Competitions.Lifecycle;
using NoCTF.GameModes.PatchVerification.Scoring;
using NoCTF.Infrastructure.Competitions.Progression;

namespace NoCTF.Infrastructure.GameplayFacts.Processing;

public sealed partial class InternalResultStore(
    NoCtfDbContext db,
    IPostCommitMessagePublisher outbox,
    ICompetitionEventRecorder? eventRecorder = null,
    ProgressionReconciler? progressionReconciler = null, NoCTF.Infrastructure.Authentication.PlatformSecretProtector? secrets = null) : IInternalResultStore
{
    private readonly ICompetitionEventRecorder events =
        eventRecorder ?? NullCompetitionEventRecorder.Instance;

    public async Task<InternalResultDisposition> RecordAwdpAsync(
        AwdpFixResult result,
        CancellationToken ct) =>
        await RecordPatchVerificationCoreAsync(result, ct);

    public async Task<InternalResultDisposition> RecordPatchVerificationAsync(
        AwdpFixResult result,
        CancellationToken ct) =>
        await RecordPatchVerificationCoreAsync(result, ct);

    private async Task<InternalResultDisposition> RecordPatchVerificationCoreAsync(
        AwdpFixResult result,
        CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(
            System.Data.IsolationLevel.Serializable, ct);
        var fact = await db.GameplayFacts.SingleOrDefaultAsync(
            item => item.Id == result.GameplayFactId,
            ct);
        if (fact is null)
            return InternalResultDisposition.NotFound;
        var runtime = await db.RuntimeInstances.SingleOrDefaultAsync(
            item => item.Id == result.RuntimeInstanceId,
            ct);
        if (runtime is null
            || runtime.Purpose is not (RuntimePurpose.AwdpTarget
                or RuntimePurpose.PatchVerificationTarget)
            || runtime.GameplayFactId != fact.Id)
            return InternalResultDisposition.NotFound;
        if (fact.State is GameplayFactState.Completed or GameplayFactState.PlatformFailed)
            return InternalResultDisposition.Duplicate;
        var context = await db.CompetitionChallenges
            .Where(challenge => challenge.Id == fact.CompetitionChallengeId)
            .Join(
                db.Competitions,
                challenge => challenge.CompetitionId,
                competition => competition.Id,
                (challenge, competition) => new { Challenge = challenge, Competition = competition })
            .Join(
                db.Challenges,
                scope => scope.Challenge.ChallengeId,
                challenge => challenge.Id,
                (scope, template) => new
                {
                    scope.Competition.Mode,
                    scope.Competition.FrozenStartAt,
                    scope.Competition.HiddenStartAt,
                    scope.Challenge.AutoOpenAt,
                    scope.Challenge.ScoringEndsAt,
                    scope.Challenge.SubmissionDeadlineAt,
                    scope.Challenge.TimingRevision,
                    IsCtfPatchVerification = template.Definition is CtfChallengeDefinition
                        && ((CtfChallengeDefinition)template.Definition).InteractionKind
                            == CtfInteractionKind.PatchVerification
                })
            .SingleAsync(ct);
        var validMode = context.Mode == GameMode.Awdp
            && runtime.Purpose == RuntimePurpose.AwdpTarget
            || context.Mode == GameMode.Ctf
            && runtime.Purpose == RuntimePurpose.PatchVerificationTarget
            && context.IsCtfPatchVerification;
        if (!validMode
            || fact.Kind != GameplayFactKind.FixAttempt
            || fact.ReferenceKind != GameplayFactReferenceKind.PatchUpload
            || fact.ReferenceId is null
            || fact.TeamId is null)
            return InternalResultDisposition.NotFound;
        var resolvedAt = ToPostgresPrecision(result.OccurredAt);
        if (result.Outcome == AwdpFixOutcome.PlatformFailed)
        {
            var convergence = await AwdpFixFailureConvergence
                .ConvergeAwdpFixFailureAsync(
                    runtime,
                    db,
                    outbox,
                    events,
                    resolvedAt,
                    AwdpFixRuntimeCleanupMode.EnsureStop,
                    ct);
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            await outbox.FlushCommittedMessagesAsync();
            return convergence.FactConverged
                ? InternalResultDisposition.Applied
                : InternalResultDisposition.Duplicate;
        }
        var decision = context.Mode == GameMode.Ctf
            ? CtfPatchVerificationOutcomeMapper.Map(result.Outcome)
            : MapAwdpOutcome(result.Outcome);
        var timing = new ChallengeTiming(context.AutoOpenAt, context.ScoringEndsAt, context.SubmissionDeadlineAt);
        fact.TimeEligibility = timing.Eligibility(fact.OccurredAt);
        fact.AppliedTimingRevision = context.TimingRevision;
        decision = decision with { Result = timing.Classify(decision.Result, fact.OccurredAt) };
        fact.Result = decision.Result;
        fact.State = GameplayFactState.Completed;
        fact.FailureCode = decision.FailureCode;
        fact.UpdatedAt = resolvedAt;
        runtime.State = RuntimeState.Stopping;
        await outbox.PublishAsync(new GameplayFactStateChanged(fact.Id, fact.State));
        var bloodAward = context.Mode == GameMode.Ctf
                && decision.Result == GameplayFactResult.Correct
            ? await TryCreateCtfPatchBloodAwardAsync(fact, resolvedAt, ct)
            : null;
        var bloodVisibility = bloodAward is null
            ? (CompetitionLeaderboardVisibility?)null
            : CompetitionLeaderboardVisibilityPolicy.EffectiveAt(
                context.FrozenStartAt,
                context.HiddenStartAt,
                bloodAward.OccurredAt);
        var announceBlood = bloodAward is not null
            && bloodVisibility != CompetitionLeaderboardVisibility.Blackout
            && CompetitionLeaderboardVisibilityPolicy.CanAnnounceBlood(
                context.FrozenStartAt,
                context.HiddenStartAt,
                resolvedAt);
        if (announceBlood && bloodAward is not null)
            await outbox.PublishAsync(bloodAward);
        if (context.Mode == GameMode.Ctf
            && NoCTF.Domain.Challenges.GameplayFactCompletion.IsSuccessful(decision.Result, fact.TimeEligibility))
        {
            var playerRuntimeIds = await db.RuntimeInstances
                .Where(instance => instance.CompetitionId == fact.CompetitionId
                    && instance.CompetitionChallengeId == fact.CompetitionChallengeId
                    && instance.TeamId == fact.TeamId
                    && (instance.Purpose == RuntimePurpose.Player
                        || instance.Purpose == RuntimePurpose.Practice)
                    && (instance.State == RuntimeState.Queued
                        || instance.State == RuntimeState.Provisioning
                        || instance.State == RuntimeState.Running
                        || instance.State == RuntimeState.Stopping))
                .Select(instance => instance.Id)
                .ToArrayAsync(ct);
            foreach (var playerRuntimeId in playerRuntimeIds)
                await outbox.PublishAsync(new StopRuntime(playerRuntimeId));
        }
        await events.RecordAsync(new(
            fact.CompetitionId,
            CompetitionEventKind.ScoringRecorded,
            result.Outcome == AwdpFixOutcome.PlatformFailed
                ? CompetitionEventLevel.Error
                : CompetitionEventLevel.Information,
            CompetitionEventVisibility.Team,
            resolvedAt,
            TeamId: fact.TeamId,
            CompetitionChallengeId: fact.CompetitionChallengeId,
            RuntimeInstanceId: runtime.Id,
            GameplayFactId: fact.Id,
            GameplayFactKind: fact.Kind,
            GameplayFactState: fact.State,
            GameplayFactResult: fact.Result), ct);
        var adjudicationEventId = await events.RecordAsync(new(
            fact.CompetitionId,
            CompetitionEventKind.GameplayFactAdjudicated,
            result.Outcome == AwdpFixOutcome.PlatformFailed
                ? CompetitionEventLevel.Error
                : CompetitionEventLevel.Information,
            context.Mode == GameMode.Ctf
                ? CompetitionEventVisibility.Public
                : CompetitionEventVisibility.Team,
            resolvedAt,
            ActorUserId: fact.ActorUserId,
            TeamId: fact.TeamId,
            CompetitionChallengeId: fact.CompetitionChallengeId,
            RuntimeInstanceId: runtime.Id,
            GameplayFactId: fact.Id,
            GameplayFactKind: fact.Kind,
            GameplayFactState: fact.State,
            GameplayFactResult: fact.Result,
            RuntimeState: runtime.State), ct);
        if (announceBlood && bloodAward is not null)
        {
            var bloodKind = bloodAward.BloodRank switch
            {
                LeaderboardBloodRank.First => CompetitionEventKind.FirstBloodAwarded,
                LeaderboardBloodRank.Second => CompetitionEventKind.SecondBloodAwarded,
                LeaderboardBloodRank.Third => CompetitionEventKind.ThirdBloodAwarded,
                _ => throw new InvalidOperationException("Unsupported leaderboard blood rank.")
            };
            await events.RecordAsync(new(
                fact.CompetitionId,
                bloodKind,
                CompetitionEventLevel.Information,
                CompetitionEventVisibility.Public,
                resolvedAt,
                TeamId: fact.TeamId,
                CompetitionChallengeId: fact.CompetitionChallengeId,
                GameplayFactId: fact.Id,
                GameplayFactKind: fact.Kind,
                GameplayFactResult: fact.Result,
                LeaderboardVisibility: bloodVisibility,
                FrozenStartAt: context.FrozenStartAt,
                ParentEventId: adjudicationEventId == Guid.Empty ? null : adjudicationEventId), ct);
        }
        if (context.Mode == GameMode.Awdp)
        {
            await events.RecordAsync(new(
                fact.CompetitionId,
                CompetitionEventKind.AwdpFixResolved,
                result.Outcome == AwdpFixOutcome.PlatformFailed
                    ? CompetitionEventLevel.Error
                    : CompetitionEventLevel.Information,
                CompetitionEventVisibility.Public,
                resolvedAt,
                TeamId: fact.TeamId,
                CompetitionChallengeId: fact.CompetitionChallengeId,
                RuntimeInstanceId: runtime.Id,
                GameplayFactId: fact.Id,
                GameplayFactKind: fact.Kind,
                GameplayFactState: fact.State,
                GameplayFactResult: fact.Result,
                PatchUploadId: fact.ReferenceId.Value,
                AwdpFixOutcome: result.Outcome,
                GameplayFactFailureCode: fact.FailureCode,
                ResolvedAt: resolvedAt), ct);
        }
        await events.RecordAsync(new(
            runtime.CompetitionId!.Value,
            CompetitionEventKind.RuntimeStateChanged,
            CompetitionEventLevel.Information,
            CompetitionEventVisibility.Team,
            resolvedAt,
            TeamId: runtime.TeamId,
            CompetitionChallengeId: runtime.CompetitionChallengeId,
            RuntimeInstanceId: runtime.Id,
            GameplayFactId: fact.Id,
            RuntimeState: RuntimeState.Stopping), ct);
        await outbox.PublishToRunnerNodeAsync(new StopContainerRuntime(
            runtime.Id,
            runtime.RunnerId
                ?? throw new InvalidOperationException("AWDP target has no owning Runner."),
            resolvedAt));
        await QueueNextAwdpFixAttemptAsync(fact, ct);
        if (context.Mode == GameMode.Ctf)
            await (progressionReconciler ?? new ProgressionReconciler(db))
                .ReconcileCompletedFactAsync(fact, resolvedAt, ct);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        await outbox.FlushCommittedMessagesAsync();
        return InternalResultDisposition.Applied;
    }

    private async Task QueueNextAwdpFixAttemptAsync(
        GameplayFact completed,
        CancellationToken ct)
    {
        var nextGameplayFactId = await db.GameplayFacts.AsNoTracking()
            .Where(candidate =>
                candidate.CompetitionId == completed.CompetitionId
                && candidate.CompetitionChallengeId == completed.CompetitionChallengeId
                && candidate.TeamId == completed.TeamId
                && candidate.Kind == GameplayFactKind.FixAttempt
                && candidate.State == GameplayFactState.Queued)
            .OrderBy(candidate => candidate.OccurredAt)
            .ThenBy(candidate => candidate.Id)
            .Select(candidate => (Guid?)candidate.Id)
            .FirstOrDefaultAsync(ct);
        if (nextGameplayFactId is Guid id)
            await outbox.PublishAsync(new EvaluateGameplayFact(id, Guid.CreateVersion7()));
    }

    private static DateTimeOffset ToPostgresPrecision(DateTimeOffset value) =>
        value.AddTicks(-(value.Ticks % TimeSpan.TicksPerMicrosecond));

    private static PatchVerificationDecision MapAwdpOutcome(AwdpFixOutcome outcome)
    {
        var decision = AwdpFixOutcomeMapper.Map(outcome);
        return new(decision.Result, decision.FailureCode);
    }

    private async Task<BloodAwarded?> TryCreateCtfPatchBloodAwardAsync(
        GameplayFact fact,
        DateTimeOffset resolvedAt,
        CancellationToken ct)
    {
        var competition = await db.Competitions.AsNoTracking()
            .Where(item => item.Id == fact.CompetitionId)
            .Select(item => new
            {
                item.Mode,
                item.StartAt,
                item.EndAt,
                item.TracksEnabled,
                item.Tracks
            })
            .SingleAsync(ct);
        if (competition.Mode != GameMode.Ctf)
            return null;
        var officialWindow = await CompetitionOfficialWindowReader.ReadAsync(
            db,
            fact.CompetitionId,
            competition.StartAt,
            competition.EndAt,
            ct);
        if (!officialWindow.Contains(fact.OccurredAt))
            return null;
        var tracks = CompetitionTrackConfiguration.EffectiveFor(
            competition.Mode,
            competition.TracksEnabled,
            competition.Tracks);
        var currentTrackKey = await db.Teams.AsNoTracking().Where(CtfCompletionEligibility.ParticipatingTeams)
            .Where(team => team.Id == fact.TeamId)
            .Select(team => team.TrackKey)
            .SingleOrDefaultAsync(ct);
        if (currentTrackKey is null) return null;
        var currentTrack = CtfCompletionEligibility.Track(tracks, currentTrackKey);
        if (currentTrack?.EarnsBlood != true)
            return null;
        if (await db.WriteUpUnlockReceipts.AsNoTracking().AnyAsync(x => x.TeamId == fact.TeamId
            && x.CompetitionChallengeId == fact.CompetitionChallengeId && x.UnlockedAt <= fact.OccurredAt, ct)) return null;
        var bloodTrackKeys = tracks.Tracks.Where(track => track.EarnsBlood)
            .Select(track => track.Key.ToLowerInvariant())
            .ToArray();
        var timing = await db.CompetitionChallenges.AsNoTracking()
            .Where(x => x.Id == fact.CompetitionChallengeId)
            .Select(x => new { x.AutoOpenAt, x.ScoringEndsAt, x.SubmissionDeadlineAt }).SingleAsync(ct);
        var solvedTeamIds = await db.GameplayFacts.AsNoTracking()
            .Where(CtfCompletionEligibility.Before(fact.OccurredAt, fact.Id))
            .Where(candidate => !db.WriteUpUnlockReceipts.Any(x => x.TeamId == candidate.TeamId
                && x.CompetitionChallengeId == candidate.CompetitionChallengeId && x.UnlockedAt <= candidate.OccurredAt))
            .Where(candidate => candidate.CompetitionId == fact.CompetitionId
                && candidate.CompetitionChallengeId == fact.CompetitionChallengeId
                && candidate.Kind == GameplayFactKind.FixAttempt
                && (candidate.Result == GameplayFactResult.Correct || candidate.Result == GameplayFactResult.RightButDue)
                && (timing.AutoOpenAt == null || candidate.OccurredAt >= timing.AutoOpenAt)
                && (timing.ScoringEndsAt == null || candidate.OccurredAt < timing.ScoringEndsAt)
                && (timing.SubmissionDeadlineAt == null || candidate.OccurredAt < timing.SubmissionDeadlineAt)
                && candidate.OccurredAt >= officialWindow.StartAt
                && candidate.OccurredAt < officialWindow.EndAt
                && candidate.Id != fact.Id)
            .Join(
                db.Teams.AsNoTracking().Where(CtfCompletionEligibility.ParticipatingTeams).Where(team => (!competition.TracksEnabled
                        || bloodTrackKeys.Contains(team.TrackKey))
                    && team.RegisteredAt < officialWindow.EndAt),
                candidate => candidate.TeamId,
                team => (Guid?)team.Id,
                (candidate, _) => candidate.TeamId!.Value)
            .Distinct()
            .ToArrayAsync(ct);
        if (fact.TeamId is Guid teamId && solvedTeamIds.Contains(teamId)
            || solvedTeamIds.Length >= 3)
            return null;
        var context = await db.CompetitionChallenges.AsNoTracking()
            .Where(challenge => challenge.Id == fact.CompetitionChallengeId)
            .Join(
                db.Challenges.AsNoTracking(),
                challenge => challenge.ChallengeId,
                template => template.Id,
                (challenge, template) => new
                {
                    Title = challenge.CustomTitle ?? template.Title
                })
            .Join(
                db.Teams.AsNoTracking().Where(team => team.Id == fact.TeamId),
                _ => fact.TeamId,
                team => team.Id,
                (challenge, team) => new { challenge.Title, TeamName = team.Name })
            .SingleAsync(ct);
        return new BloodAwarded(
            fact.CompetitionId,
            fact.CompetitionChallengeId,
            context.Title,
            (LeaderboardBloodRank)(solvedTeamIds.Length + 1),
            fact.TeamId!.Value,
            context.TeamName,
            resolvedAt);
    }
}
