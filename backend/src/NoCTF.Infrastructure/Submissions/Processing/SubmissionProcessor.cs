using NoCTF.Infrastructure.Persistence;
using System.Data;
using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Messaging;
using NoCTF.Application.Runtime.Provisioning;
using NoCTF.Application.Submissions.Intake;
using NoCTF.Application.Submissions.Processing;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Runtime;
using NoCTF.Domain.Submissions;
using NoCTF.GameModes.Awd.Scheduling;
using NoCTF.GameModes.Awdp.Configuration;
using NoCTF.GameModes.Awdp.Runtime;

namespace NoCTF.Infrastructure.Submissions.Processing;

public sealed class SubmissionProcessor(
    NoCtfDbContext db,
    ISubmissionEvaluatorCatalog evaluatorCatalog,
    ISubmissionAdmissionModePolicy admissionModePolicy,
    IRuntimePlacementPolicy placementPolicy,
    ITransactionalMessageOutbox outbox) : ISubmissionProcessor
{
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
                || configuration.Competition.Mode == GameMode.Awd)
            .ToListAsync(cancellationToken);
        var patch = submission.PatchUploadId is { } patchUploadId
            ? await db.PatchUploads.AsNoTracking()
                .SingleOrDefaultAsync(item => item.Id == patchUploadId, cancellationToken)
            : null;
        TimeSpan? effectiveRunningTime = null;
        if (configuration.Competition.Mode == GameMode.Awd)
        {
            var lifecycle = await db.Set<CompetitionLifecycleAudit>().AsNoTracking()
                .Where(audit => audit.CompetitionId == submission.CompetitionId
                    && audit.OccurredAt <= submission.ReceivedAt)
                .ToListAsync(cancellationToken);
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
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return;
        }

        if (submission.CurrentScoringEventId is { } currentEventId)
        {
            var current = await db.ScoringEvents.IgnoreQueryFilters()
                .SingleAsync(item => item.Id == currentEventId, cancellationToken);
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
            Kind = ScoringEventKind.SubmissionEvaluation,
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
        submission.CurrentScoringEventId = scoringEvent.Id;
        submission.EvaluationState = SubmissionEvaluationState.Completed;
        submission.EvaluationFailureCode = null;
        submission.EvaluationUpdatedAt = now;
        var competition = await db.Competitions.SingleAsync(
            item => item.Id == submission.CompetitionId, cancellationToken);
        competition.LeaderboardRevision = checked(competition.LeaderboardRevision + 1);
        await outbox.PublishAsync(new ProjectLeaderboard(submission.CompetitionId));
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        await outbox.FlushOutgoingMessagesAsync();
    }

    private sealed record Evaluation(
        ScoringEventDecision Decision,
        int CompetitionRevision,
        int CompetitionChallengeRevision,
        int ChallengeDefinitionRevision);
}
