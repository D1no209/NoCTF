using NoCTF.Application.Messaging;
using NoCTF.Application.Storage;
using NoCTF.Application.Scoring.Leaderboard;
using NoCTF.Application.GameplayFacts.Processing;
using NoCTF.Domain.Platform;
using FluentStorage.Storage;
using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Runtime.Instances;
using NoCTF.Application.Runtime.Provisioning;
using NoCTF.Application.Runtime.Capacity;
using NoCTF.Application.Competitions.Awd;
using NoCTF.Application.Competitions.Koh;
using NoCTF.Application.Competitions.Visibility;
using NoCTF.Application.Authentication.EmailVerification;
using NoCTF.Application.Authentication.PasswordReset;
using NoCTF.Domain.Runtime;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Challenges;
using NoCTF.Infrastructure.Persistence;
using NoCTF.Domain.Notifications;
using System.Text.Json;
using System.Buffers.Binary;
using System.Security.Cryptography;
using NoCTF.GameModes.Awd.Configuration;
using NoCTF.GameModes.Awdp.Configuration;
using NoCTF.GameModes.Awdp.Runtime;
using NoCTF.GameModes.Registration;
using NoCTF.Worker.Runtime;
using CompetitionLifecycleAdvancer = NoCTF.Application.Competitions.Lifecycle.AdvanceCompetitionLifecycleUseCase;
using NoCTF.Application.Competitions.Events;
using NoCTF.Domain.Competitions.Events;
using NoCTF.Domain.Shared;
using NoCTF.Domain.Gameplay;

namespace NoCTF.Worker;

internal static partial class BackendMessageOperations
{
    public static async Task DispatchRuntimeAsync(
        DispatchRuntime message,
        NoCtfDbContext db,
        IChallengeRuntimeTemplateCatalog templates,
        IRuntimePlacementPolicy placementPolicy,
        IRunnerCapacityGate capacity,
        ITransactionalMessageOutbox outbox,
        TimeProvider timeProvider,
        CancellationToken cancellationToken,
        ICompetitionEventRecorder? events = null)
    {
        events ??= NullCompetitionEventRecorder.Instance;
        var target = await db.RuntimeInstances
            .Join(
                db.CompetitionChallenges,
                instance => instance.CompetitionChallengeId,
                challenge => challenge.Id,
                (instance, challenge) => new { Instance = instance, Challenge = challenge })
            .Join(
                db.Competitions,
                pair => pair.Instance.CompetitionId,
                competition => competition.Id,
                (pair, competition) => new { pair.Instance, pair.Challenge, Competition = competition })
            .Join(
                db.Challenges,
                item => item.Challenge.ChallengeId,
                challenge => challenge.Id,
                (item, challenge) => new
                {
                    item.Instance,
                    item.Challenge,
                    item.Competition,
                    Template = challenge
                })
            .SingleOrDefaultAsync(item => item.Instance.Id == message.RuntimeInstanceId, cancellationToken);
        if (target is null || target.Instance.State != RuntimeState.Queued)
            return;

        var awdpConfiguration = target.Instance.Purpose == RuntimePurpose.AwdpTarget
            ? AwdpConfigurationResolver.Resolve(
                target.Competition.ConfigurationJson,
                target.Challenge.RulesJson,
                target.Template.DefinitionJson)
            : null;
        var template = awdpConfiguration?.Runtime
            ?? templates.Get(target.Competition.Mode, target.Template.DefinitionJson);
        if (template is null)
        {
            target.Instance.State = RuntimeState.Failed;
            target.Instance.FailureCode = RuntimeFailureCode.InvalidConfiguration;
            await FailAwdpSubmissionAsync(
                target.Instance, db, outbox, timeProvider, cancellationToken);
            await RecordRuntimeStateAsync(
                events,
                target.Instance,
                CompetitionEventLevel.Error,
                timeProvider.GetUtcNow(),
                cancellationToken);
            await db.SaveChangesAsync(cancellationToken);
            await outbox.FlushOutgoingMessagesAsync();
            return;
        }

        string? perTeamFlag = null;
        if (target.Competition.Mode is GameMode.Ctf or GameMode.Awdp
            && target.Instance.Purpose != RuntimePurpose.AwdpTarget
            && template.FlagSource == RuntimeFlagSource.PerTeam)
        {
            if (target.Instance.TeamId is Guid teamId)
            {
                var specificationKind = target.Competition.Mode == GameMode.Awdp
                    ? SpecificationKind.RuntimeInstance
                    : SpecificationKind.RuntimeDefinition;
                var specificationId = target.Competition.Mode == GameMode.Awdp
                    ? target.Instance.Id
                    : target.Challenge.Id;
                perTeamFlag = await db.ChallengeFlags.AsNoTracking()
                    .Where(flag =>
                        flag.CompetitionChallengeId == target.Challenge.Id
                        && flag.TeamId == teamId
                        && flag.SpecificationKind == specificationKind
                        && flag.SpecificationId == specificationId
                        && flag.DeletedAt == null
                        && flag.ValidUntil == null)
                    .Select(flag => flag.Flag)
                    .SingleOrDefaultAsync(cancellationToken);
            }
            if (perTeamFlag is null)
            {
                target.Instance.State = RuntimeState.Failed;
                target.Instance.FailureCode = RuntimeFailureCode.InvalidConfiguration;
                await RecordRuntimeStateAsync(
                    events,
                    target.Instance,
                    CompetitionEventLevel.Error,
                    timeProvider.GetUtcNow(),
                    cancellationToken);
                await db.SaveChangesAsync(cancellationToken);
                await outbox.FlushOutgoingMessagesAsync();
                return;
            }
        }

        var placement = placementPolicy.Resolve(target.Instance.RuntimeKind);
        if (placement.Provider != target.Instance.RuntimeProvider)
        {
            target.Instance.State = RuntimeState.Failed;
            target.Instance.FailureCode = RuntimeFailureCode.InvalidConfiguration;
            await FailAwdpSubmissionAsync(
                target.Instance, db, outbox, timeProvider, cancellationToken);
            await db.SaveChangesAsync(cancellationToken);
            await outbox.FlushOutgoingMessagesAsync();
            return;
        }

        var limits = template.Limits
            ?? new RuntimeResourceLimits(512 * 1024 * 1024, 500_000_000, 256);
        var capacityClaim = await capacity.TryClaimAsync(new RunnerCapacityRequest(
            target.Instance.Id,
            placement.RunnerPool,
            limits.MemoryBytes,
            limits.NanoCpus,
            limits.PidsLimit), cancellationToken);
        if (capacityClaim.Availability != RunnerCapacityAvailability.Claimed
            || string.IsNullOrWhiteSpace(capacityClaim.RunnerId))
        {
            var retryAt = timeProvider.GetUtcNow().Add(RunnerDependencyRetryDelay);
            await outbox.ScheduleAsync(message, retryAt);
            await outbox.FlushOutgoingMessagesAsync();
            return;
        }

        var runnerId = capacityClaim.RunnerId;
        IRuntimeProvisionMessage provision;
        try
        {
            if (target.Instance.Purpose == RuntimePurpose.AwdpTarget)
            {
                var definition = AwdpTargetDefinitionFactory.Create(
                    target.Instance.Id,
                    template,
                    target.Instance.RuntimeProvider,
                    timeProvider.GetUtcNow());
                if (target.Instance.RuntimeProvider == RuntimeProvider.Docker)
                {
                    definition = definition with
                    {
                        PortMappings = definition.PortMappings.Keys.ToDictionary(
                            port => port,
                            _ => 0)
                    };
                }
                provision = new ProvisionContainerRuntime(
                    target.Instance.Id,
                    runnerId,
                    definition);
            }
            else
            {
                provision = RuntimeClaimFactory.Create(
                    target.Instance,
                    runnerId,
                    target.Competition.Mode,
                    template,
                    target.Template.DefinitionJson,
                    perTeamFlag);
            }
        }
        catch (InvalidOperationException)
        {
            var release = await capacity.ReleaseAsync(
                target.Instance.Id,
                runnerId,
                cancellationToken);
            if (release == RunnerCapacityReleaseOutcome.OwnerMismatch)
            {
                throw new InvalidOperationException(
                    "The selected Runner no longer owns the Runtime capacity claim.");
            }
            target.Instance.State = RuntimeState.Failed;
            target.Instance.FailureCode = RuntimeFailureCode.InvalidConfiguration;
            await FailAwdpSubmissionAsync(
                target.Instance, db, outbox, timeProvider, cancellationToken);
            await RecordRuntimeStateAsync(
                events,
                target.Instance,
                CompetitionEventLevel.Error,
                timeProvider.GetUtcNow(),
                cancellationToken);
            await db.SaveChangesAsync(cancellationToken);
            await outbox.FlushOutgoingMessagesAsync();
            return;
        }

        target.Instance.RunnerId = runnerId;
        target.Instance.State = RuntimeState.Provisioning;
        target.Instance.FailureCode = null;
        await PublishRuntimeProvisionAsync(outbox, provision);
        await db.SaveChangesAsync(cancellationToken);
        await outbox.FlushOutgoingMessagesAsync();
    }

    private static async Task FailAwdpSubmissionAsync(
        RuntimeInstance instance,
        NoCtfDbContext db,
        ITransactionalMessageOutbox outbox,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        if (instance.Purpose != RuntimePurpose.AwdpTarget
            || instance.GameplayFactId is not Guid gameplayFactId)
            return;
        var submission = await db.GameplayFacts.SingleOrDefaultAsync(
            item => item.Id == gameplayFactId,
            cancellationToken);
        if (submission is null
            || submission.State != NoCTF.Domain.Gameplay.GameplayFactState.Processing)
            return;
        submission.State = NoCTF.Domain.Gameplay.GameplayFactState.PlatformFailed;
        submission.FailureCode = NoCTF.Domain.Gameplay.GameplayFactFailureCode.CheckerPlatformError;
        submission.UpdatedAt = timeProvider.GetUtcNow();
        await outbox.PublishAsync(new GameplayFactStateChanged(submission.Id, submission.State));
        await QueueNextGameplayFactAsync(submission, db, outbox, cancellationToken);
    }

    private static async Task QueueNextGameplayFactAsync(
        GameplayFact completed,
        NoCtfDbContext db,
        ITransactionalMessageOutbox outbox,
        CancellationToken cancellationToken)
    {
        var nextGameplayFactId = await db.GameplayFacts.AsNoTracking()
            .Where(candidate =>
                candidate.CompetitionId == completed.CompetitionId
                && candidate.CompetitionChallengeId == completed.CompetitionChallengeId
                && candidate.TeamId == completed.TeamId
                && candidate.Kind == completed.Kind
                && candidate.State == GameplayFactState.Queued)
            .OrderBy(candidate => candidate.OccurredAt)
            .ThenBy(candidate => candidate.Id)
            .Select(candidate => (Guid?)candidate.Id)
            .FirstOrDefaultAsync(cancellationToken);
        if (nextGameplayFactId is Guid id)
            await outbox.PublishAsync(new EvaluateGameplayFact(id));
    }

}
