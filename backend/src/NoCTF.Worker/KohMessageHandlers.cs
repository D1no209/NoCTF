using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Globalization;
using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Competitions.Koh;
using NoCTF.Application.Competitions.Events;
using NoCTF.Application.Messaging;
using NoCTF.Application.Runtime.Provisioning;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Competitions.Events;
using NoCTF.Domain.Runtime;
using NoCTF.Domain.Gameplay;
using NoCTF.Domain.Challenges;
using NoCTF.GameModes.Koh.Configuration;
using NoCTF.Infrastructure.Persistence;
using Wolverine.Attributes;
using NoCTF.Application.Challenges.Capabilities;
using NoCTF.Domain.Challenges.Capabilities;

namespace NoCTF.Worker;

[NonTransactional]
public sealed class KohPollingHandler(
    NoCtfDbContext db,
    ObserveControl control,
    IKohProducerConfigurationCatalog configurations,
    TimeProvider timeProvider)
{
    public async Task<RecordKohObservation?> Handle(
        PollKohChallenge message,
        CancellationToken cancellationToken)
    {
        var target = await db.CompetitionChallenges.AsNoTracking()
            .Where(challenge => challenge.Id == message.CompetitionChallengeId
                && challenge.CompetitionId == message.CompetitionId
                && challenge.IsPublished
                && challenge.DeletedAt == null)
            .Join(
                db.Competitions.AsNoTracking(),
                challenge => challenge.CompetitionId,
                competition => competition.Id,
                (challenge, competition) => new { Challenge = challenge, Competition = competition })
            .AsSplitQuery()
            .SingleOrDefaultAsync(cancellationToken);
        if (target is null
            || target.Competition.Mode != GameMode.Koh
            || target.Competition.Status != CompetitionStatus.Running
            || target.Competition.ModeConfiguration is not KohCompetitionModeConfiguration competitionConfiguration
            || target.Challenge.Rules is not KohCompetitionChallengeRules challengeRules)
            return null;

        var settings = configurations.Get(
            competitionConfiguration,
            challengeRules);
        var timeout = TimeSpan.FromSeconds(Math.Min(settings.PollIntervalSeconds, 30));
        var runtime = await db.RuntimeInstances.AsNoTracking()
            .Where(runtime => runtime.CompetitionId == message.CompetitionId
                && runtime.CompetitionChallengeId == message.CompetitionChallengeId
                && runtime.TeamId == null
                && runtime.State == RuntimeState.Running
                && runtime.ProviderReceipt != null)
            .Join(
                db.CompetitionChallenges.AsNoTracking(),
                instance => instance.CompetitionChallengeId,
                challenge => challenge.Id,
                (instance, challenge) => new { instance, challenge.ChallengeId })
            .Join(
                db.Challenges.AsNoTracking(),
                pair => pair.ChallengeId,
                challenge => challenge.Id,
                (pair, challenge) => new
                {
                    Runtime = pair.instance,
                    Template = challenge
                })
            .OrderByDescending(item => item.Runtime.CreatedAt)
            .AsSplitQuery()
            .FirstOrDefaultAsync(cancellationToken);
        var controlUrl = runtime is null
            ? null
            : ResolveControlUrl(
                runtime.Template.Definition,
                runtime.Runtime.RuntimeKind,
                runtime.Runtime.RuntimeProvider,
                runtime.Runtime.ProviderReceipt!);

        var definition = runtime?.Template.Definition?.Control
            ?? await db.Set<ControlCapabilityDefinition>().AsNoTracking().SingleOrDefaultAsync(value => value.ChallengeId == target.Challenge.ChallengeId, cancellationToken);
        if (definition is null) return null;
        var execution = await control.ExecuteAsync(new(message.GameplayFactId, runtime?.Runtime.Id, definition,
            controlUrl, timeProvider.GetUtcNow() + timeout), cancellationToken);
        var outcome = execution.Value?.ToGameplayOutcome() ?? new ControlOutcome
        {
            GameplayFactId = message.GameplayFactId, RuntimeInstanceId = runtime?.Runtime.Id, ObservedAt = timeProvider.GetUtcNow(),
            Result = ControlResult.Indeterminate, ExecutionState = CapabilityExecutionState.PlatformFailed, ExecutionFailure = execution.FailureCode
        };
        var observedAt = outcome.ObservedAt;
        var decision = await DecideAsync(
            message.CompetitionChallengeId,
            outcome,
            observedAt,
            cancellationToken);
        return new(
            message.GameplayFactId,
            message.CompetitionId,
            message.CompetitionChallengeId,
            decision.TeamId,
            decision.Result,
            decision.FailureCode,
            message.RunningSince,
            message.DueAt,
            observedAt,
            outcome);
    }

    private static Uri? ResolveControlUrl(
        ChallengeDefinition? definition,
        RuntimeKind runtimeKind,
        RuntimeProvider provider,
        RuntimeReceipt providerReceipt)
    {
        var binding = definition?.Control;
        if (binding is null || runtimeKind != RuntimeKind.Container)
            return null;
        if (providerReceipt is not ContainerRuntimeReceipt receipt
            || receipt.Provider != provider
            || string.IsNullOrWhiteSpace(receipt.PublicHost)
            || binding.ContainerPort is not int port)
            return null;
        var service = receipt.Services.SingleOrDefault(service => service.Name == binding.ServiceName);
        if (string.IsNullOrWhiteSpace(service?.InternalHost)) return null;
        var expanded = binding.UrlTemplate.Replace("{HOST}", service.InternalHost, StringComparison.OrdinalIgnoreCase)
            .Replace("{PORT}", port.ToString(CultureInfo.InvariantCulture), StringComparison.OrdinalIgnoreCase);
        return Uri.TryCreate(expanded, UriKind.Absolute, out var uri) ? uri : null;
    }

    private async Task<KohDecision> DecideAsync(
        Guid competitionChallengeId,
        ControlOutcome outcome,
        DateTimeOffset observedAt,
        CancellationToken cancellationToken)
    {
        if (outcome.ExecutionFailure == CapabilityExecutionFailure.DeadlineExceeded)
            return new(null, null, GameplayFactFailureCode.ProducerTimeout);
        if (outcome.Result == ControlResult.Indeterminate)
            return new(null, null, GameplayFactFailureCode.ProducerUnavailable);
        if (outcome.Result == ControlResult.Uncontrolled)
            return new(null, GameplayFactResult.Uncontrolled, null);
        var flag = outcome.ControllerIdentifier ?? throw new InvalidOperationException("A controlled observation requires an identifier.");
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(flag));
        var matches = await db.ChallengeFlags.AsNoTracking()
            .Where(candidate => candidate.CompetitionChallengeId == competitionChallengeId
                && candidate.TeamId != null
                && candidate.SpecificationKind == NoCTF.Domain.Challenges.SpecificationKind.RuntimeDefinition
                && candidate.SpecificationId == competitionChallengeId
                && candidate.FlagSha256 == hash
                && candidate.Flag == flag
                && candidate.DeletedAt == null
                && (candidate.ValidStart == null || candidate.ValidStart <= observedAt)
                && (candidate.ValidUntil == null || candidate.ValidUntil > observedAt))
            .Select(candidate => candidate.TeamId!.Value)
            .Distinct()
            .OrderBy(teamId => teamId)
            .Take(2)
            .ToArrayAsync(cancellationToken);
        return matches.Length switch
        {
            0 => new(null, GameplayFactResult.Uncontrolled, null),
            1 => new(matches[0], GameplayFactResult.Controlled, null),
            _ => new(null, null, GameplayFactFailureCode.AmbiguousFlagMatch)
        };
    }

    private sealed record KohDecision(
        Guid? TeamId,
        GameplayFactResult? Result,
        GameplayFactFailureCode? FailureCode);
}

public sealed class KohObservationHandler(
    NoCtfDbContext db,
    IPostCommitMessagePublisher outbox,
    ICompetitionEventRecorder events,
    TimeProvider timeProvider)
{
    public async Task Handle(
        RecordKohObservation message,
        CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var target = await db.CompetitionChallenges.AsNoTracking()
            .Where(challenge => challenge.Id == message.CompetitionChallengeId
                && challenge.CompetitionId == message.CompetitionId
                && challenge.IsPublished
                && challenge.DeletedAt == null)
            .Join(
                db.Competitions.AsNoTracking(),
                challenge => challenge.CompetitionId,
                competition => competition.Id,
                (challenge, competition) => new
                {
                    ChallengePublished = challenge.IsPublished,
                    competition.Mode,
                    competition.Status
                })
            .SingleOrDefaultAsync(cancellationToken);
        if (target is null
            || target.Mode != GameMode.Koh
            || target.Status != CompetitionStatus.Running
            || !target.ChallengePublished)
            return;

        if (await db.GameplayFacts.AsNoTracking()
            .AnyAsync(fact => fact.Id == message.GameplayFactId, cancellationToken))
            return;

        if (message.Outcome.GameplayFactId != message.GameplayFactId || message.Outcome.ObservedAt != message.ObservedAt)
            throw new InvalidOperationException("The control evidence does not match its observation.");
        if (message.Outcome.RuntimeInstanceId is { } runtimeId && !await db.RuntimeInstances.AnyAsync(runtime => runtime.Id == runtimeId
                && runtime.CompetitionId == message.CompetitionId && runtime.CompetitionChallengeId == message.CompetitionChallengeId
                && runtime.State == RuntimeState.Running, cancellationToken))
            return;

        db.GameplayFacts.Add(new ControlObservationGameplayFact
        {
            Id = message.GameplayFactId,
            CompetitionId = message.CompetitionId,
            CompetitionChallengeId = message.CompetitionChallengeId,
            TeamId = message.TeamId,
            State = message.Result is null
                ? GameplayFactState.PlatformFailed
                : GameplayFactState.Completed,
            Result = message.Result,
            FailureCode = message.FailureCode,
            CapabilityOutcomes = [message.Outcome],
            OccurredAt = message.ObservedAt,
            UpdatedAt = timeProvider.GetUtcNow()
        });
        await events.RecordAsync(new(
            message.CompetitionId,
            CompetitionEventKind.GameplayFactAdjudicated,
            message.Result is null
                ? CompetitionEventLevel.Error
                : CompetitionEventLevel.Information,
            CompetitionEventVisibility.Staff,
            message.ObservedAt,
            TeamId: message.TeamId,
            CompetitionChallengeId: message.CompetitionChallengeId,
            GameplayFactId: message.GameplayFactId,
            GameplayFactKind: GameplayFactKind.ControlObservation,
            GameplayFactState: message.Result is null
                ? GameplayFactState.PlatformFailed
                : GameplayFactState.Completed,
            GameplayFactResult: message.Result), cancellationToken);
        // The cluster Singular Agent derives the next poll from this observation.
        // Do not persist recursive scheduled messages as a second scheduler.
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        await outbox.FlushCommittedMessagesAsync();
    }
}
