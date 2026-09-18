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

namespace NoCTF.Infrastructure.GameplayFacts.Processing;

public sealed class InternalResultStore(
    NoCtfDbContext db,
    ITransactionalMessageOutbox outbox,
    ICompetitionEventRecorder? eventRecorder = null) : IInternalResultStore
{
    private readonly ICompetitionEventRecorder events =
        eventRecorder ?? NullCompetitionEventRecorder.Instance;

    public async Task<InternalResultDisposition> RecordAwdAsync(
        AwdCheckResult result,
        CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var fact = await db.GameplayFacts
            .FromSqlInterpolated(
                $"SELECT * FROM gameplay_facts WHERE id = {result.GameplayFactId} FOR UPDATE")
            .SingleOrDefaultAsync(ct);
        if (fact is null)
            return InternalResultDisposition.NotFound;
        var runtime = await db.RuntimeInstances.SingleOrDefaultAsync(
            item => item.Id == result.RuntimeInstanceId,
            ct);
        if (runtime is null)
            return InternalResultDisposition.NotFound;
        if (fact.State is GameplayFactState.Completed or GameplayFactState.PlatformFailed)
            return InternalResultDisposition.Duplicate;
        var appliedAt = ToPostgresPrecision(result.OccurredAt);

        var context = await db.CompetitionChallenges
            .Where(challenge => challenge.Id == runtime.CompetitionChallengeId)
            .Join(
                db.Competitions,
                challenge => challenge.CompetitionId,
                competition => competition.Id,
                (challenge, competition) => new
                {
                    Competition = competition
                })
            .SingleAsync(ct);
        if (context.Competition.Mode != GameMode.Awd
            || runtime.TeamId is null
            || fact.Kind != GameplayFactKind.AwdServiceTransition
            || fact.CompetitionId != runtime.CompetitionId
            || fact.CompetitionChallengeId != runtime.CompetitionChallengeId
            || fact.TeamId != runtime.TeamId)
            return InternalResultDisposition.NotFound;
        var scoringResult = AwdCheckerOutcomeMapper.ToGameplayFactResult(result.State);
        fact.State = GameplayFactState.Completed;
        fact.Result = scoringResult;
        fact.FailureCode = result.State is AwdServiceState.CheckerAbnormalExit
            or AwdServiceState.CheckerTimedOut
                ? GameplayFactFailureCode.CheckerPlatformError
                : null;
        fact.UpdatedAt = appliedAt;
        await events.RecordAsync(new(
            runtime.CompetitionId!.Value,
            CompetitionEventKind.ScoringRecorded,
            scoringResult == GameplayFactResult.ServiceDown
                ? CompetitionEventLevel.Warning
                : CompetitionEventLevel.Information,
            CompetitionEventVisibility.Team,
            appliedAt,
            TeamId: fact.TeamId,
            CompetitionChallengeId: fact.CompetitionChallengeId,
            RuntimeInstanceId: runtime.Id,
            GameplayFactId: fact.Id,
            GameplayFactKind: GameplayFactKind.AwdServiceTransition,
            GameplayFactState: fact.State,
            GameplayFactResult: fact.Result,
            RuntimeState: runtime.State), ct);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        await outbox.FlushOutgoingMessagesAsync();
        return InternalResultDisposition.Applied;
    }

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
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var fact = await db.GameplayFacts
            .FromSqlInterpolated(
                $"SELECT * FROM gameplay_facts WHERE id = {result.GameplayFactId} FOR UPDATE")
            .SingleOrDefaultAsync(ct);
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
                    scope.Challenge,
                    scope.Competition,
                    template.DefinitionJson
                })
            .SingleAsync(ct);
        var validMode = context.Competition.Mode == GameMode.Awdp
            && runtime.Purpose == RuntimePurpose.AwdpTarget
            || context.Competition.Mode == GameMode.Ctf
            && runtime.Purpose == RuntimePurpose.PatchVerificationTarget
            && CtfConfigurationUpgrader.ParseChallenge(context.DefinitionJson).InteractionKind
                == CtfInteractionKind.PatchVerification;
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
            await outbox.FlushOutgoingMessagesAsync();
            return convergence.FactConverged
                ? InternalResultDisposition.Applied
                : InternalResultDisposition.Duplicate;
        }
        var decision = context.Competition.Mode == GameMode.Ctf
            ? CtfPatchVerificationOutcomeMapper.Map(result.Outcome)
            : MapAwdpOutcome(result.Outcome);
        fact.Result = decision.Result;
        fact.State = GameplayFactState.Completed;
        fact.FailureCode = decision.FailureCode;
        fact.UpdatedAt = resolvedAt;
        runtime.State = RuntimeState.Stopping;
        await outbox.PublishAsync(new GameplayFactStateChanged(fact.Id, fact.State));
        var bloodAward = context.Competition.Mode == GameMode.Ctf
                && decision.Result == GameplayFactResult.Correct
            ? await TryCreateCtfPatchBloodAwardAsync(fact, resolvedAt, ct)
            : null;
        if (bloodAward is not null)
            await outbox.PublishAsync(bloodAward);
        if (context.Competition.Mode == GameMode.Ctf
            && decision.Result == GameplayFactResult.Correct)
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
        await events.RecordAsync(new(
            fact.CompetitionId,
            CompetitionEventKind.GameplayFactAdjudicated,
            result.Outcome == AwdpFixOutcome.PlatformFailed
                ? CompetitionEventLevel.Error
                : CompetitionEventLevel.Information,
            context.Competition.Mode == GameMode.Ctf
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
        if (bloodAward is not null)
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
                GameplayFactResult: fact.Result), ct);
        }
        if (context.Competition.Mode == GameMode.Awdp)
        {
            var payload = AwdpFixResolvedEventPayload.Create(
                fact.Id,
                fact.ReferenceId.Value,
                runtime.Id,
                fact.TeamId.Value,
                fact.CompetitionChallengeId,
                result.Outcome,
                fact.FailureCode,
                resolvedAt);
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
                PayloadJson: payload.Serialize()), ct);
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
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        await outbox.FlushOutgoingMessagesAsync();
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
            await outbox.PublishAsync(new EvaluateGameplayFact(id));
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
                item.TrackConfigurationJson
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
            competition.TrackConfigurationJson);
        var currentTrackKey = await db.Teams.AsNoTracking().Where(CtfCompletionEligibility.ParticipatingTeams)
            .Where(team => team.Id == fact.TeamId)
            .Select(team => team.TrackKey)
            .SingleOrDefaultAsync(ct);
        if (currentTrackKey is null) return null;
        var currentTrack = CtfCompletionEligibility.Track(tracks, currentTrackKey);
        if (currentTrack?.EarnsBlood != true)
            return null;
        var bloodTrackKeys = tracks.Tracks.Where(track => track.EarnsBlood)
            .Select(track => track.Key.ToLowerInvariant())
            .ToArray();
        var solvedTeamIds = await db.GameplayFacts.AsNoTracking()
            .Where(CtfCompletionEligibility.Before(fact.OccurredAt, fact.Id))
            .Where(candidate => candidate.CompetitionId == fact.CompetitionId
                && candidate.CompetitionChallengeId == fact.CompetitionChallengeId
                && candidate.Kind == GameplayFactKind.FixAttempt
                && candidate.Result == GameplayFactResult.Correct
                && candidate.OccurredAt >= officialWindow.StartAt
                && candidate.OccurredAt < officialWindow.EndAt
                && candidate.Id != fact.Id)
            .Join(
                db.Teams.AsNoTracking().Where(CtfCompletionEligibility.ParticipatingTeams).Where(team => (!competition.TracksEnabled
                        || bloodTrackKeys.Contains(team.TrackKey.ToLower()))
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
