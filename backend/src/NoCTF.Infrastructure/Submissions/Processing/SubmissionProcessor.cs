using NoCTF.Infrastructure.Persistence;
using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NoCTF.Application.Messaging;
using NoCTF.Application.Runtime.Provisioning;
using NoCTF.Application.Scoring.Leaderboard;
using NoCTF.Application.Submissions.Intake;
using NoCTF.Application.Submissions.Processing;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Challenges;
using NoCTF.Domain.Runtime;
using NoCTF.Domain.Submissions;
using NoCTF.GameModes.Awd.Scheduling;
using NoCTF.GameModes.Awdp.Configuration;
using NoCTF.GameModes.Awdp.Runtime;
using NoCTF.Application.Competitions.Events;
using NoCTF.Domain.Competitions.Events;
using NoCTF.Domain.Shared;
using System.Globalization;
using System.Text.Json;

namespace NoCTF.Infrastructure.Submissions.Processing;

public sealed class SubmissionProcessor(
    NoCtfDbContext db,
    ISubmissionEvaluatorCatalog evaluatorCatalog,
    ISubmissionAdmissionModePolicy admissionModePolicy,
    IRuntimePlacementPolicy placementPolicy,
    ITransactionalMessageOutbox outbox,
    ILeaderboardSnapshotFactory leaderboardSnapshots,
    BloodRankCriticalSection bloodRankCriticalSection,
    ICompetitionEventRecorder? eventRecorder = null,
    ILogger<SubmissionProcessor>? logger = null) : ISubmissionProcessor
{
    public SubmissionProcessor(
        NoCtfDbContext db,
        ISubmissionEvaluatorCatalog evaluatorCatalog,
        ISubmissionAdmissionModePolicy admissionModePolicy,
        IRuntimePlacementPolicy placementPolicy,
        ITransactionalMessageOutbox outbox,
        ILeaderboardSnapshotFactory leaderboardSnapshots,
        ICompetitionEventRecorder? eventRecorder = null,
        ILogger<SubmissionProcessor>? logger = null)
        : this(
            db,
            evaluatorCatalog,
            admissionModePolicy,
            placementPolicy,
            outbox,
            leaderboardSnapshots,
            new BloodRankCriticalSection(new LocalCriticalSectionRegistry()),
            eventRecorder,
            logger) { }

    private static readonly CompetitionEventKind[] CheatResolutionKinds =
    [
        CompetitionEventKind.CheatIncidentConfirmed,
        CompetitionEventKind.CheatIncidentDismissed,
        CompetitionEventKind.CheatIncidentSuperseded,
        CompetitionEventKind.CheatIncidentCorrected
    ];

    private readonly ICompetitionEventRecorder events =
        eventRecorder ?? NullCompetitionEventRecorder.Instance;
    private readonly ILogger<SubmissionProcessor> log =
        logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<SubmissionProcessor>.Instance;

    public async Task ProcessAsync(
        Guid submissionId,
        long processingVersion,
        CancellationToken cancellationToken)
    {
        if (!await ClaimAsync(submissionId, processingVersion, cancellationToken))
            return;

        var evaluation = await EvaluateAsync(submissionId, cancellationToken);
        await CompleteAsync(submissionId, checked(processingVersion + 1), evaluation, cancellationToken);
    }

    private async Task<bool> ClaimAsync(
        Guid submissionId,
        long expectedVersion,
        CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(
            IsolationLevel.ReadCommitted, cancellationToken);
        var submission = await db.Submissions.SingleOrDefaultAsync(
            item => item.Id == submissionId, cancellationToken);
        if (submission is null
            || submission.EvaluationState != SubmissionEvaluationState.Queued
            || submission.ProcessingVersion != expectedVersion)
            return false;
        submission.EvaluationState = SubmissionEvaluationState.Processing;
        submission.ProcessingVersion = checked(submission.ProcessingVersion + 1);
        submission.EvaluationFailureCode = null;
        submission.EvaluationUpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        db.ChangeTracker.Clear();
        return true;
    }

    private async Task<Evaluation> EvaluateAsync(Guid submissionId, CancellationToken cancellationToken)
    {
        var submission = await db.Submissions.AsNoTracking()
            .SingleAsync(item => item.Id == submissionId, cancellationToken);
        var configuration = await db.Competitions.AsNoTracking()
            .Where(competition => competition.Id == submission.CompetitionId)
            .Join(
                db.CompetitionChallenges.AsNoTracking()
                    .Where(challenge => challenge.Id == submission.CompetitionChallengeId),
                competition => competition.Id,
                challenge => challenge.CompetitionId,
                (competition, challenge) => new
                {
                    Competition = competition,
                    CompetitionChallenge = challenge
                })
            .Join(
                db.Challenges.AsNoTracking(),
                scope => scope.CompetitionChallenge.ChallengeId,
                challenge => challenge.Id,
                (scope, challenge) => new
                {
                    scope.Competition,
                    scope.CompetitionChallenge,
                    Challenge = challenge
                })
            .SingleAsync(cancellationToken);
        var rules = admissionModePolicy.GetRules(
            configuration.Competition.Mode,
            configuration.Competition.ConfigurationJson,
            configuration.CompetitionChallenge.RulesJson);
        if (submission.Kind is SubmissionKind.HintUnlock or SubmissionKind.ManualAdjust)
        {
            var special = await EvaluateSpecialAsync(
                submission,
                configuration.CompetitionChallenge,
                DateTimeOffset.UtcNow,
                cancellationToken);
            return new(
                special,
                configuration.Competition.ConfigurationRevision,
                configuration.CompetitionChallenge.Revision,
                configuration.Challenge.Revision);
        }
        var maxAttempts = submission.Kind is SubmissionKind.Flag or SubmissionKind.Break
            ? rules.MaxFlagAttempts
            : rules.MaxFixAttempts;
        if (maxAttempts is > 0)
        {
            var acceptedIds = await db.Submissions.AsNoTracking()
                .Where(candidate =>
                    candidate.CompetitionId == submission.CompetitionId
                    && candidate.TeamId == submission.TeamId
                    && candidate.CompetitionChallengeId == submission.CompetitionChallengeId
                    && candidate.Kind == submission.Kind
                    && candidate.EvaluationState != SubmissionEvaluationState.PlatformFailed)
                .OrderBy(candidate => candidate.ReceivedAt)
                .ThenBy(candidate => candidate.Id)
                .Select(candidate => candidate.Id)
                .ToListAsync(cancellationToken);
            if (acceptedIds.IndexOf(submission.Id) + 1 > maxAttempts)
                return new(
                    new(
                        ScoringEventKind.SubmissionEvaluation,
                        ScoringResult.AttemptsExhausted,
                        null,
                        submission.ReceivedAt,
                        "attempt-limit-v2"),
                    configuration.Competition.ConfigurationRevision,
                    configuration.CompetitionChallenge.Revision,
                    configuration.Challenge.Revision);
        }

        var priorEvents = await db.ScoringEvents.AsNoTracking()
            .Where(@event =>
                @event.CompetitionId == submission.CompetitionId
                && @event.SubmissionId != submission.Id)
            .ToListAsync(cancellationToken);
        var priorSubmissions = await db.Submissions.AsNoTracking()
            .Where(item => item.CompetitionId == submission.CompetitionId && item.Id != submission.Id)
            .ToListAsync(cancellationToken);
        var flags = await db.ChallengeFlags.AsNoTracking()
            .Where(flag =>
                flag.CompetitionChallengeId == submission.CompetitionChallengeId
                || flag.ChallengeId == configuration.CompetitionChallenge.ChallengeId)
            .Where(flag => flag.TeamId == null || flag.TeamId == submission.TeamId
                || configuration.Competition.Mode == GameMode.Ctf
                || configuration.Competition.Mode == GameMode.Awd
                || configuration.Competition.Mode == GameMode.Awdp)
            .ToListAsync(cancellationToken);
        var patch = submission.PatchUploadId is { } patchUploadId
            ? await db.PatchUploads.AsNoTracking()
                .SingleOrDefaultAsync(item => item.Id == patchUploadId, cancellationToken)
            : null;
        TimeSpan? effectiveRunningTime = null;
        if (configuration.Competition.Mode == GameMode.Awd)
        {
            var lifecycleEvents = await db.CompetitionEvents.AsNoTracking()
                .Where(@event => @event.CompetitionId == submission.CompetitionId
                    && @event.Kind == CompetitionEventKind.CompetitionLifecycleChanged
                    && @event.OccurredAt <= submission.ReceivedAt)
                .OrderBy(@event => @event.OccurredAt)
                .ToListAsync(cancellationToken);
            var lifecycle = lifecycleEvents.Select(@event =>
            {
                using var payload = JsonDocument.Parse(@event.PayloadJson);
                return new CompetitionLifecycleTransition
                {
                    Id = @event.Id,
                    CompetitionId = @event.CompetitionId,
                    From = Enum.Parse<CompetitionStatus>(payload.RootElement.GetProperty("from").GetString()!, true),
                    To = Enum.Parse<CompetitionStatus>(payload.RootElement.GetProperty("to").GetString()!, true),
                    ActorId = @event.ActorUserId,
                    OccurredAt = @event.OccurredAt
                };
            }).ToList();
            effectiveRunningTime = AwdEffectiveRunningClock.Calculate(
                lifecycle, submission.ReceivedAt);
        }
        var decision = evaluatorCatalog.Get(configuration.Competition.Mode).Evaluate(new(
            submission,
            priorEvents,
            flags,
            patch,
            configuration.Competition.ConfigurationJson,
            configuration.CompetitionChallenge.RulesJson,
            priorSubmissions,
            configuration.Competition.StartAt,
            effectiveRunningTime));
        return new(
            decision,
            configuration.Competition.ConfigurationRevision,
            configuration.CompetitionChallenge.Revision,
            configuration.Challenge.Revision);
    }

    private async Task<ScoringEventDecision> EvaluateSpecialAsync(
        Submission submission,
        NoCTF.Domain.Challenges.CompetitionChallenge challenge,
        DateTimeOffset projectedAt,
        CancellationToken ct)
    {
        if (submission.Kind == SubmissionKind.ManualAdjust)
        {
            if (!int.TryParse(
                    submission.SubmittedFlag,
                    NumberStyles.AllowLeadingSign,
                    CultureInfo.InvariantCulture,
                    out var delta)
                || delta == 0
                || delta.ToString(CultureInfo.InvariantCulture) != submission.SubmittedFlag)
            {
                return new(
                    ScoringEventKind.ManualAdjust,
                    ScoringResult.Rejected,
                    ScoringFailureCode.HintUnavailable,
                    submission.ReceivedAt,
                    "manual-adjust-v1");
            }
            return new(
                ScoringEventKind.ManualAdjust,
                ScoringResult.Correct,
                null,
                submission.ReceivedAt,
                "manual-adjust-v1");
        }

        if (!Guid.TryParseExact(submission.SubmittedFlag, "D", out var hintId))
            return new(ScoringEventKind.HintUnlock, ScoringResult.Rejected,
                ScoringFailureCode.HintUnavailable, submission.ReceivedAt, "hint-unlock-v1");
        var hint = challenge.Hints.SingleOrDefault(item =>
            item.Id == hintId
            && item.PublishedAt is not null
            && item.PublishedAt <= submission.ReceivedAt
            && item.HiddenAt is null);
        if (hint is null || !challenge.IsPublished)
            return new(ScoringEventKind.HintUnlock, ScoringResult.Rejected,
                ScoringFailureCode.HintUnavailable, submission.ReceivedAt, "hint-unlock-v1");

        var alreadyUnlocked = await db.Submissions.AsNoTracking()
            .Where(item => item.CompetitionId == submission.CompetitionId
                && item.TeamId == submission.TeamId
                && item.Kind == SubmissionKind.HintUnlock
                && item.Id != submission.Id
                && item.CurrentScoringEventId != null)
            .Join(db.ScoringEvents.AsNoTracking(), item => item.CurrentScoringEventId,
                scoring => (Guid?)scoring.Id, (item, scoring) => new { item, scoring })
            .AnyAsync(item => item.scoring.DeletedAt == null
                && item.scoring.Result == ScoringResult.Correct
                && item.item.SubmittedFlag == submission.SubmittedFlag, ct);
        if (alreadyUnlocked)
            return new(ScoringEventKind.HintUnlock, ScoringResult.Duplicate,
                null, submission.ReceivedAt, "hint-unlock-v1",
                SpecificationKind: SpecificationKind.Hint, SpecificationId: hintId);

        var score = await AuthoritativeScoreBeforeSubmissionAsync(
            submission,
            hint.Cost,
            projectedAt,
            ct);
        if (score < hint.Cost)
            return new(ScoringEventKind.HintUnlock, ScoringResult.Rejected,
                ScoringFailureCode.InsufficientScore, submission.ReceivedAt, "hint-unlock-v1",
                SpecificationKind: SpecificationKind.Hint, SpecificationId: hintId);
        return new(ScoringEventKind.HintUnlock, ScoringResult.Correct, null,
            submission.ReceivedAt, "hint-unlock-v1",
            SpecificationKind: SpecificationKind.Hint, SpecificationId: hintId);
    }

    private async Task<long> AuthoritativeScoreBeforeSubmissionAsync(
        Submission submission,
        long currentHintCost,
        DateTimeOffset projectedAt,
        CancellationToken ct)
    {
        var snapshot = await leaderboardSnapshots.CreateAsync(
                submission.CompetitionId,
                projectedAt,
                historical: false,
                ct)
            ?? throw new InvalidOperationException(
                $"Competition {submission.CompetitionId} has no leaderboard projection.");
        var score = snapshot.Entries
            .SingleOrDefault(entry => entry.TeamId == submission.TeamId)?.Score ?? 0;
        if (submission.CurrentScoringEventId is not Guid currentEventId)
            return score;

        var currentEventContributes = await db.ScoringEvents.AsNoTracking().AnyAsync(
            scoringEvent => scoringEvent.Id == currentEventId
                && scoringEvent.DeletedAt == null
                && scoringEvent.Kind == ScoringEventKind.HintUnlock
                && scoringEvent.Result == ScoringResult.Correct,
            ct);
        return currentEventContributes
            ? checked(score + currentHintCost)
            : score;
    }

    private async Task CompleteAsync(
        Guid submissionId,
        long processingVersion,
        Evaluation evaluation,
        CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(
            IsolationLevel.ReadCommitted, cancellationToken);
        var submission = await db.Submissions.SingleOrDefaultAsync(
            item => item.Id == submissionId, cancellationToken);
        if (submission is null
            || submission.EvaluationState != SubmissionEvaluationState.Processing
            || submission.ProcessingVersion != processingVersion)
            return;

        var now = DateTimeOffset.UtcNow;
        if (submission.Kind is SubmissionKind.HintUnlock or SubmissionKind.ManualAdjust)
        {
            await AcquireTeamScoringLockAsync(
                submission.CompetitionId,
                submission.TeamId,
                cancellationToken);
            var specialScope = await db.Competitions.AsNoTracking()
                .Where(competition => competition.Id == submission.CompetitionId)
                .Join(
                    db.CompetitionChallenges.AsNoTracking()
                        .Where(challenge => challenge.Id == submission.CompetitionChallengeId),
                    competition => competition.Id,
                    challenge => challenge.CompetitionId,
                    (competition, challenge) => new { Competition = competition, Challenge = challenge })
                .SingleAsync(cancellationToken);
            evaluation = evaluation with
            {
                Decision = await EvaluateSpecialAsync(
                    submission,
                    specialScope.Challenge,
                    now,
                    cancellationToken),
                CompetitionRevision = specialScope.Competition.ConfigurationRevision,
                CompetitionChallengeRevision = specialScope.Challenge.Revision
            };
        }

        if (evaluation.Decision.Result == ScoringResult.PlatformFailed)
        {
            if (submission.Kind == SubmissionKind.Fix)
            {
                var configurationJson = await db.CompetitionChallenges.AsNoTracking()
                    .Where(challenge => challenge.Id == submission.CompetitionChallengeId)
                    .Join(
                        db.Competitions.AsNoTracking(),
                        challenge => challenge.CompetitionId,
                        competition => competition.Id,
                        (challenge, competition) => new
                        {
                            Competition = competition.ConfigurationJson,
                            Challenge = challenge
                        })
                    .Join(
                        db.Challenges.AsNoTracking(),
                        scope => scope.Challenge.ChallengeId,
                        template => template.Id,
                        (scope, template) => new
                        {
                            scope.Competition,
                            Rules = scope.Challenge.RulesJson,
                            template.DefinitionJson
                        })
                    .SingleAsync(cancellationToken);
                var definition = AwdpConfigurationParser.ParseChallenge(
                    configurationJson.DefinitionJson);
                var rules = AwdpConfigurationParser.ParseChallenge(configurationJson.Rules);
                var combined = rules with
                {
                    Runtime = definition.Runtime,
                    Checker = definition.Checker,
                    PatchEntrypoint = definition.PatchEntrypoint,
                    PatchCommand = definition.PatchCommand,
                    PatchTimeoutSeconds = definition.PatchTimeoutSeconds,
                    ReadyTimeoutSeconds = definition.ReadyTimeoutSeconds
                };
                var configuration = AwdpConfigurationResolver.Resolve(
                    configurationJson.Competition,
                    System.Text.Json.JsonSerializer.Serialize(
                        combined,
                        new System.Text.Json.JsonSerializerOptions(
                            System.Text.Json.JsonSerializerDefaults.Web)));
                if (configuration.Runtime is { } template
                    && submission.PatchUploadId is not null)
                {
                    var targetId = Guid.CreateVersion7(now);
                    var configurationIsValid = true;
                    try
                    {
                        var placement = placementPolicy.Resolve(template.RuntimeKind);
                        _ = AwdpTargetDefinitionFactory.Create(
                            targetId,
                            1,
                            template,
                            placement.Provider,
                            now);
                    }
                    catch (InvalidOperationException)
                    {
                        configurationIsValid = false;
                    }

                    if (configurationIsValid)
                    {
                        var generation = checked((await db.RuntimeInstances
                            .Where(instance => instance.SubmissionId == submission.Id)
                            .MaxAsync(instance => (int?)instance.Generation, cancellationToken) ?? 0) + 1);
                        var target = AwdpTargetRuntimeFactory.Create(
                            submission.Id,
                            submission.CompetitionId,
                            submission.CompetitionChallengeId,
                            targetId,
                            template,
                            placementPolicy.Resolve(template.RuntimeKind),
                            generation,
                            submission.ProcessingVersion,
                            evaluation.CompetitionRevision,
                            evaluation.CompetitionChallengeRevision,
                            evaluation.ChallengeDefinitionRevision,
                            now);
                        db.RuntimeInstances.Add(target);
                        await outbox.PublishAsync(new DispatchRuntime(
                            target.Id,
                            target.ProcessingVersion));
                        await events.RecordAsync(new(
                            target.CompetitionId,
                            CompetitionEventKind.RuntimeCreated,
                            CompetitionEventLevel.Information,
                            CompetitionEventVisibility.Team,
                            now,
                            TeamId: target.TeamId,
                            CompetitionChallengeId: target.CompetitionChallengeId,
                            RuntimeInstanceId: target.Id,
                            SubmissionId: submission.Id,
                            RuntimeState: target.State,
                            RuntimeGeneration: target.Generation), cancellationToken);
                        submission.EvaluationFailureCode = null;
                        submission.EvaluationUpdatedAt = now;
                        await db.SaveChangesAsync(cancellationToken);
                        await transaction.CommitAsync(cancellationToken);
                        await outbox.FlushOutgoingMessagesAsync();
                        return;
                    }
                }
            }
            submission.EvaluationState = SubmissionEvaluationState.PlatformFailed;
            submission.EvaluationFailureCode =
                evaluation.Decision.FailureCode ?? ScoringFailureCode.CheckerPlatformError;
            submission.EvaluationUpdatedAt = now;
            await events.RecordAsync(new(
                submission.CompetitionId,
                CompetitionEventKind.SubmissionEvaluated,
                CompetitionEventLevel.Error,
                CompetitionEventVisibility.Team,
                now,
                ActorUserId: submission.SubmittedByUserId,
                TeamId: submission.TeamId,
                CompetitionChallengeId: submission.CompetitionChallengeId,
                SubmissionId: submission.Id,
                SubmissionKind: submission.Kind,
                SubmissionState: submission.EvaluationState,
                ScoringResult: ScoringResult.PlatformFailed), cancellationToken);
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return;
        }

        if (submission.CurrentScoringEventId is { } currentEventId)
        {
            var current = await db.ScoringEvents
                .IgnoreQueryFilters()
                .SingleAsync(item => item.Id == currentEventId, cancellationToken);
            if (current.FailureCode == ScoringFailureCode.ForeignTeamFlagDetected)
            {
                var resolved = await db.CompetitionEvents.AsNoTracking().AnyAsync(
                    @event => (@event.SubjectType == EntityReferenceKind.ScoringEvent
                            && @event.SubjectId == current.Id
                            || @event.RelatedType == EntityReferenceKind.ScoringEvent
                            && @event.RelatedId == current.Id)
                        && CheatResolutionKinds.Contains(@event.Kind),
                    cancellationToken);
                if (!resolved)
                {
                    await events.RecordAsync(new(
                        submission.CompetitionId,
                        CompetitionEventKind.CheatIncidentSuperseded,
                        CompetitionEventLevel.Information,
                        CompetitionEventVisibility.Staff,
                        now,
                        TeamId: current.TeamId,
                        CompetitionChallengeId: current.CompetitionChallengeId,
                        SubmissionId: current.SubmissionId,
                        ScoringEventId: current.Id,
                        ScoringEventKind: current.Kind,
                        ScoringResult: current.Result,
                        Reason: "Superseded by submission re-evaluation."), cancellationToken);
                }
            }
            current.DeletedAt = now;
        }
        var scoringEvent = new ScoringEvent
        {
            Id = Guid.CreateVersion7(now),
            CompetitionId = submission.CompetitionId,
            CompetitionChallengeId = submission.CompetitionChallengeId,
            SubmissionId = submission.Id,
            TeamId = submission.TeamId,
            VictimTeamId = evaluation.Decision.VictimTeamId,
            Kind = evaluation.Decision.Kind,
            Result = evaluation.Decision.Result,
            FailureCode = evaluation.Decision.FailureCode,
            SpecificationKind = evaluation.Decision.SpecificationKind,
            SpecificationId = evaluation.Decision.SpecificationId,
            ProcessingVersion = submission.ProcessingVersion,
            CompetitionConfigurationRevision = evaluation.CompetitionRevision,
            CompetitionChallengeRevision = evaluation.CompetitionChallengeRevision,
            OccurredAt = evaluation.Decision.OccurredAt,
            CreatedAt = now
        };
        db.ScoringEvents.Add(scoringEvent);
        if (scoringEvent.FailureCode == ScoringFailureCode.ForeignTeamFlagDetected
            && scoringEvent.VictimTeamId is Guid ownerTeamId)
        {
            await outbox.PublishAsync(new ForeignTeamFlagDetected(
                submission.CompetitionId,
                scoringEvent.Id,
                submission.Id,
                submission.TeamId,
                ownerTeamId,
                submission.SubmittedByUserId,
                submission.CompetitionChallengeId,
                scoringEvent.OccurredAt));
            await events.RecordAsync(new(
                submission.CompetitionId,
                CompetitionEventKind.CheatIncidentDetected,
                CompetitionEventLevel.Warning,
                CompetitionEventVisibility.Staff,
                scoringEvent.OccurredAt,
                ActorUserId: submission.SubmittedByUserId,
                TeamId: submission.TeamId,
                CompetitionChallengeId: submission.CompetitionChallengeId,
                SubmissionId: submission.Id,
                ScoringEventId: scoringEvent.Id,
                SubmissionKind: submission.Kind,
                ScoringEventKind: scoringEvent.Kind,
                ScoringResult: scoringEvent.Result), cancellationToken);
            log.LogWarning(
                "Foreign team Flag detected for competition {CompetitionId}, submission {SubmissionId}, source team {SourceTeamId}, owner team {OwnerTeamId}.",
                submission.CompetitionId,
                submission.Id,
                submission.TeamId,
                ownerTeamId);
        }
        else if (scoringEvent.FailureCode == ScoringFailureCode.AmbiguousFlagMatch)
        {
            log.LogWarning(
                "Ambiguous Flag ownership detected for competition {CompetitionId}, submission {SubmissionId}, source team {SourceTeamId}.",
                submission.CompetitionId,
                submission.Id,
                submission.TeamId);
        }
        submission.CurrentScoringEventId = scoringEvent.Id;
        submission.EvaluationState = SubmissionEvaluationState.Completed;
        submission.EvaluationFailureCode = null;
        submission.EvaluationUpdatedAt = now;
        await LeaderboardRevision.IncrementAsync(
            db,
            submission.CompetitionId,
            cancellationToken);
        var bloodAward = await TryCreateBloodAwardAsync(
            submission,
            evaluation,
            cancellationToken);
        await outbox.PublishAsync(new InvalidateLeaderboard(submission.CompetitionId));
        if (bloodAward is not null)
            await outbox.PublishAsync(bloodAward);
        await events.RecordAsync(new(
            submission.CompetitionId,
            CompetitionEventKind.SubmissionEvaluated,
            evaluation.Decision.Result == ScoringResult.PlatformFailed
                ? CompetitionEventLevel.Error
                : CompetitionEventLevel.Information,
            CompetitionEventVisibility.Team,
            now,
            ActorUserId: submission.SubmittedByUserId,
            TeamId: submission.TeamId,
            CompetitionChallengeId: submission.CompetitionChallengeId,
            SubmissionId: submission.Id,
            SubmissionKind: submission.Kind,
            SubmissionState: submission.EvaluationState,
            ScoringResult: scoringEvent.Result), cancellationToken);
        await events.RecordAsync(new(
            submission.CompetitionId,
            CompetitionEventKind.ScoringRecorded,
            scoringEvent.Result == ScoringResult.PlatformFailed
                ? CompetitionEventLevel.Error
                : CompetitionEventLevel.Information,
            CompetitionEventVisibility.Team,
            scoringEvent.OccurredAt,
            TeamId: scoringEvent.TeamId,
            CompetitionChallengeId: scoringEvent.CompetitionChallengeId,
            SubmissionId: scoringEvent.SubmissionId,
            ScoringEventId: scoringEvent.Id,
            ScoringEventKind: scoringEvent.Kind,
            ScoringResult: scoringEvent.Result), cancellationToken);
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
                submission.CompetitionId,
                bloodKind,
                CompetitionEventLevel.Information,
                CompetitionEventVisibility.Public,
                bloodAward.OccurredAt,
                TeamId: submission.TeamId,
                CompetitionChallengeId: submission.CompetitionChallengeId,
                SubmissionId: submission.Id,
                SubmissionKind: submission.Kind,
                ScoringEventKind: scoringEvent.Kind,
                ScoringResult: scoringEvent.Result), cancellationToken);
        }
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        await outbox.FlushOutgoingMessagesAsync();
    }

    private Task AcquireTeamScoringLockAsync(
        Guid competitionId,
        Guid teamId,
        CancellationToken ct) =>
        db.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT 1 FROM pg_advisory_xact_lock(hashtextextended({competitionId.ToString() + ":" + teamId}, 0))",
            ct);

    private async Task<BloodAwarded?> TryCreateBloodAwardAsync(
        Submission submission,
        Evaluation evaluation,
        CancellationToken ct)
    {
        if (submission.Kind != SubmissionKind.Flag
            || evaluation.Decision.Result != ScoringResult.Correct)
            return null;

        var competitionMode = await db.Competitions.AsNoTracking()
            .Where(competition => competition.Id == submission.CompetitionId)
            .Select(competition => competition.Mode)
            .SingleAsync(ct);
        if (competitionMode != GameMode.Ctf)
            return null;

        await using var bloodLease = await bloodRankCriticalSection.AcquireAsync(
            db,
            submission.CompetitionChallengeId,
            ct);
        var solvedTeamIds = await db.Submissions.AsNoTracking()
            .Where(candidate =>
                candidate.CompetitionId == submission.CompetitionId
                && candidate.CompetitionChallengeId == submission.CompetitionChallengeId
                && candidate.Kind == SubmissionKind.Flag
                && candidate.CurrentScoringEventId != null
                && candidate.Id != submission.Id)
            .Join(
                db.ScoringEvents.AsNoTracking().Where(@event =>
                    @event.DeletedAt == null
                    && @event.Result == ScoringResult.Correct),
                candidate => candidate.CurrentScoringEventId,
                @event => (Guid?)@event.Id,
                (candidate, _) => candidate.TeamId)
            .Distinct()
            .ToArrayAsync(ct);
        if (solvedTeamIds.Contains(submission.TeamId) || solvedTeamIds.Length >= 3)
            return null;

        var context = await db.CompetitionChallenges.AsNoTracking()
            .Where(challenge => challenge.Id == submission.CompetitionChallengeId)
            .Join(
                db.Challenges.AsNoTracking(),
                challenge => challenge.ChallengeId,
                template => template.Id,
                (challenge, template) => new { template.Title })
            .Join(
                db.Teams.AsNoTracking().Where(team => team.Id == submission.TeamId),
                _ => submission.TeamId,
                team => team.Id,
                (challenge, team) => new { challenge.Title, TeamName = team.Name })
            .SingleAsync(ct);
        return new BloodAwarded(
            submission.CompetitionId,
            submission.CompetitionChallengeId,
            context.Title,
            (LeaderboardBloodRank)(solvedTeamIds.Length + 1),
            submission.TeamId,
            context.TeamName,
            evaluation.Decision.OccurredAt);
    }

    private sealed record Evaluation(
        ScoringEventDecision Decision,
        int CompetitionRevision,
        int CompetitionChallengeRevision,
        int ChallengeDefinitionRevision);
}
