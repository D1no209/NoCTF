using System.Data;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Challenges.Capabilities;
using NoCTF.Application.Competitions.Events;
using NoCTF.Application.GameplayFacts.Processing;
using NoCTF.Application.Messaging;
using NoCTF.Application.Runtime.Provisioning;
using NoCTF.Domain.Challenges;
using NoCTF.Domain.Challenges.Capabilities;
using NoCTF.Domain.Challenges.Testing;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Competitions.Events;
using NoCTF.Domain.Gameplay;
using NoCTF.Domain.Runtime;
using NoCTF.Infrastructure.Persistence;
using NoCTF.Infrastructure.Competitions.Lifecycle;

namespace NoCTF.Infrastructure.Challenges.Capabilities;

public sealed class ControlExecutionStore(IDbContextFactory<NoCtfDbContext> contexts,
    IPostCommitMessagePublisher messages, TimeProvider clock) : IControlResultStore
{
    public async Task AcceptAsync(PollKohChallenge command, CancellationToken ct)
    {
        await using var db = await contexts.CreateDbContextAsync(ct);
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        if (await db.GameplayFacts.AnyAsync(fact => fact.Id == command.GameplayFactId, ct)) return;
        var challenge = await db.CompetitionChallenges.AsNoTracking().AsSplitQuery().SingleOrDefaultAsync(value =>
            value.Id == command.CompetitionChallengeId && value.CompetitionId == command.CompetitionId
            && value.IsPublished && value.DeletedAt == null, ct);
        if (challenge is null) return;
        var competition = await db.Competitions.AsNoTracking().Where(value => value.Id == command.CompetitionId
            && value.Mode == GameMode.Koh && value.Status == CompetitionStatus.Running && value.DeletedAt == null)
            .Select(value => new { value.StartAt, value.EndAt }).SingleOrDefaultAsync(ct);
        if (competition is null) return;
        var active = await CompetitionEffectiveRuntimeReader.ReadAsync(db, command.CompetitionId,
            competition.StartAt, competition.EndAt, clock.GetUtcNow(), ct);
        if (active.RunningSince != command.RunningSince || command.DueAt < command.RunningSince) return;
        if (await db.GameplayFacts.AnyAsync(value => value.CompetitionChallengeId == challenge.Id
            && value.ControlExecution != null && value.ControlExecution.FinalizedAt == null, ct)) return;
        var definition = await db.Set<ControlCapabilityDefinition>().AsNoTracking().AsSplitQuery()
            .SingleOrDefaultAsync(value => value.ChallengeId == challenge.ChallengeId, ct);
        if (definition is null) return;
        var runtime = await db.RuntimeInstances.AsNoTracking().AsSplitQuery()
            .Where(value => value.CompetitionChallengeId == challenge.Id && value.TeamId == null && value.State == RuntimeState.Running)
            .OrderByDescending(value => value.CreatedAt).FirstOrDefaultAsync(ct);
        var now = clock.GetUtcNow();
        var fact = new ControlObservationGameplayFact
        {
            Id = command.GameplayFactId, CompetitionId = command.CompetitionId, CompetitionChallengeId = challenge.Id,
            OccurredAt = command.DueAt, UpdatedAt = now, State = GameplayFactState.Queued
        };
        if (runtime is not { RunnerId: not null, RuntimeKind: RuntimeKind.Container, ProviderReceipt: not null }
            || string.IsNullOrWhiteSpace(definition.Image))
        {
            fact.State = GameplayFactState.PlatformFailed; fact.FailureCode = GameplayFactFailureCode.ProducerUnavailable;
            fact.CapabilityOutcomes.Add(new ControlOutcome { GameplayFactId = fact.Id, RuntimeInstanceId = runtime?.Id,
                ObservedAt = now, Result = ControlResult.Indeterminate, ExecutionState = CapabilityExecutionState.PlatformFailed,
                ExecutionFailure = CapabilityExecutionFailure.RuntimeUnavailable });
        }
        else
        {
            var deadline = now.AddSeconds(Math.Clamp(definition.TimeoutSeconds, 1, 3600));
            deadline = deadline.AddTicks(-(deadline.Ticks % TimeSpan.TicksPerMicrosecond));
            fact.ControlExecution = new() { ExecutionId = fact.Id, RuntimeInstanceId = runtime.Id,
                RunnerId = runtime.RunnerId, Deadline = deadline };
            await messages.PublishToRunnerNodeAsync(new RunControlObservation(fact.Id, runtime.Id,
                CapabilityExecutionScope.GameplayFact, fact.ControlExecution.Deadline, runtime.RunnerId));
        }
        db.GameplayFacts.Add(fact);
        if (fact.State == GameplayFactState.PlatformFailed)
            await RecordDecisionEventAsync(db, fact, now, ct);
        await db.SaveChangesAsync(ct); await transaction.CommitAsync(ct); await messages.FlushCommittedMessagesAsync();
    }

    public async Task<ControlExecutionInput?> ClaimAsync(RunControlObservation command, CancellationToken ct)
    {
        await using var db = await contexts.CreateDbContextAsync(ct);
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var root = await LoadAsync(db, command.ExecutionId, command.RuntimeInstanceId, command.Scope, ct);
        if (root is null || root.Test?.CancellationRequested == true || root.Execution.StartedAt is not null || root.Execution.FinalizedAt is not null
            || root.Execution.RunnerId != command.RunnerId || root.Execution.Deadline != command.Deadline
            || clock.GetUtcNow() >= command.Deadline || root.Runtime.State != RuntimeState.Running
            || root.Runtime.RunnerId != command.RunnerId || root.Runtime.ProviderReceipt?.ToData() is not ContainerRuntimeReceiptData receipt) return null;
        var challengeId = root.Runtime.ChallengeId ?? await db.CompetitionChallenges.Where(value => value.Id == root.Runtime.CompetitionChallengeId)
            .Select(value => value.ChallengeId).SingleAsync(ct);
        var definition = await db.Set<ControlCapabilityDefinition>().AsNoTracking().AsSplitQuery()
            .SingleOrDefaultAsync(value => value.ChallengeId == challengeId, ct);
        if (definition is null || string.IsNullOrWhiteSpace(definition.Image)) return null;
        root.Execution.StartedAt = clock.GetUtcNow();
        if (root.Fact is { } fact) fact.State = GameplayFactState.Processing;
        if (root.Test is { } test) test.State = TemplateCapabilityTestState.Checking;
        root.Touch(clock.GetUtcNow());
        await db.SaveChangesAsync(ct); await transaction.CommitAsync(ct);
        return new(command.ExecutionId, command.RuntimeInstanceId, command.Scope, receipt, definition, command.Deadline);
    }

    public async Task<InternalResultDisposition> RecordAsync(Guid executionId, Guid runtimeId, CapabilityExecutionScope scope,
        string runnerId, DateTimeOffset deadline, ControlEvidence evidence, CancellationToken ct)
    {
        await using var db = await contexts.CreateDbContextAsync(ct);
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var root = await LoadAsync(db, executionId, runtimeId, scope, ct);
        if (root is null) return InternalResultDisposition.NotFound;
        var execution = root.Execution;
        var observedAt = evidence.ObservedAt.AddTicks(-(evidence.ObservedAt.Ticks % TimeSpan.TicksPerMicrosecond));
        if (execution.RunnerId != runnerId || execution.Deadline != deadline || root.Runtime.RunnerId != runnerId
            || root.Runtime.State != RuntimeState.Running || root.Test?.CancellationRequested == true
            || execution.StartedAt is null || clock.GetUtcNow() > deadline)
            return InternalResultDisposition.Superseded;
        if (ControlEvidenceValidation.Validate(evidence, execution.StartedAt.Value, deadline, clock.GetUtcNow()) is not null)
            return InternalResultDisposition.Conflict;
        if (execution.ReceivedAt is not null)
            return execution.Result == evidence.Result && execution.ControllerIdentifier == evidence.ControllerIdentifier
                && execution.ObservedAt == observedAt && execution.ReportedFailure == evidence.Failure
                ? InternalResultDisposition.Duplicate : InternalResultDisposition.Conflict;
        if (execution.FinalizedAt is not null || execution.WorkCompletedAt is not null)
            return InternalResultDisposition.Superseded;
        execution.Result = evidence.Result; execution.ControllerIdentifier = evidence.ControllerIdentifier;
        execution.Failure = evidence.Failure; execution.ObservedAt = observedAt; execution.ReceivedAt = clock.GetUtcNow();
        execution.ReportedFailure = evidence.Failure;
        root.Touch(clock.GetUtcNow());
        await db.SaveChangesAsync(ct); await transaction.CommitAsync(ct);
        return InternalResultDisposition.Applied;
    }

    public async Task CompleteAsync(RunControlObservation command, CapabilityExecutionFailure? failure, CancellationToken ct)
    {
        await using var db = await contexts.CreateDbContextAsync(ct);
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var root = await LoadAsync(db, command.ExecutionId, command.RuntimeInstanceId, command.Scope, ct);
        if (root is null || root.Execution.FinalizedAt is not null || root.Execution.RunnerId != command.RunnerId) return;
        root.Execution.WorkCompletedAt ??= clock.GetUtcNow();
        if (failure is not null) root.Execution.Failure = failure;
        else root.Execution.Failure ??= root.Execution.ReceivedAt is null ? CapabilityExecutionFailure.MissingEvidence : null;
        if (root.Test is { } test) test.State = TemplateCapabilityTestState.Finalizing;
        root.Touch(clock.GetUtcNow());
        await db.SaveChangesAsync(ct); await transaction.CommitAsync(ct);
        await messages.PublishAsync(new FinalizeControlObservation(command.ExecutionId, command.RuntimeInstanceId, command.Scope));
        await messages.FlushCommittedMessagesAsync();
    }

    public async Task<bool> IsCurrentAsync(RunControlObservation command, CancellationToken ct)
    {
        await using var db = await contexts.CreateDbContextAsync(ct);
        var root = await LoadAsync(db, command.ExecutionId, command.RuntimeInstanceId, command.Scope, ct);
        return root is not null && root.Runtime.State == RuntimeState.Running && root.Runtime.RunnerId == command.RunnerId
            && root.Test?.CancellationRequested != true && root.Execution.WorkCompletedAt is null
            && root.Execution.FinalizedAt is null && clock.GetUtcNow() < root.Execution.Deadline;
    }

    public async Task FinalizeAsync(FinalizeControlObservation command, CancellationToken ct)
    {
        await using var db = await contexts.CreateDbContextAsync(ct);
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var root = await LoadAsync(db, command.ExecutionId, command.RuntimeInstanceId, command.Scope, ct);
        if (root is null || root.Execution.FinalizedAt is not null || root.Execution.WorkCompletedAt is null) return;
        var kind = command.Scope == CapabilityExecutionScope.TemplateTest ? RuntimeWorkloadKind.TemplateChecker : RuntimeWorkloadKind.ControlObserver;
        if (root.Runtime.CapacityAllocations.Items.Any(value => value.Identity.Kind == kind && value.Identity.OperationId == command.ExecutionId)) return;
        var execution = root.Execution;
        if (root.Runtime.State != RuntimeState.Running) execution.Failure ??= CapabilityExecutionFailure.TargetSuperseded;
        if (execution.ReceivedAt is null) execution.Failure ??= CapabilityExecutionFailure.MissingEvidence;
        execution.FinalizedAt = clock.GetUtcNow();
        if (root.Test is { } test)
        {
            test.State = test.CancellationRequested ? TemplateCapabilityTestState.Canceled
                : execution.Failure is null ? TemplateCapabilityTestState.Completed : TemplateCapabilityTestState.PlatformFailed;
            test.CompletedAt = clock.GetUtcNow(); test.Failure ??= execution.Failure;
        }
        if (root.Fact is { } fact)
        {
            var result = execution.Failure is null ? execution.Result ?? ControlResult.Indeterminate : ControlResult.Indeterminate;
            var outcome = new ControlOutcome { GameplayFactId = fact.Id, RuntimeInstanceId = root.Runtime.Id,
                ObservedAt = execution.ObservedAt ?? clock.GetUtcNow(), Result = result,
                ControllerIdentifier = result == ControlResult.Controlled ? execution.ControllerIdentifier : null,
                ExecutionState = execution.Failure is null ? CapabilityExecutionState.Completed : CapabilityExecutionState.PlatformFailed,
                ExecutionFailure = execution.Failure };
            var alreadyRecorded = fact.CapabilityOutcomes.OfType<ControlOutcome>().Any();
            if (!alreadyRecorded) fact.CapabilityOutcomes.Add(outcome);
            if (!alreadyRecorded) fact.OccurredAt = outcome.ObservedAt;
            if (execution.Failure is not null)
            {
                fact.State = GameplayFactState.PlatformFailed; fact.Result = null;
                fact.FailureCode = execution.Failure == CapabilityExecutionFailure.DeadlineExceeded
                    ? GameplayFactFailureCode.ProducerTimeout : GameplayFactFailureCode.ProducerUnavailable;
            }
            else
            {
                var teams = result != ControlResult.Controlled ? [] : await MatchTeamsAsync(db, fact.CompetitionChallengeId!.Value,
                    execution.ControllerIdentifier!, outcome.ObservedAt, ct);
                fact.State = teams.Length > 1 ? GameplayFactState.PlatformFailed : GameplayFactState.Completed;
                fact.Result = teams.Length > 1 ? null : teams.Length == 1 ? GameplayFactResult.Controlled : GameplayFactResult.Uncontrolled;
                fact.TeamId = teams.Length == 1 ? teams[0] : null;
                fact.FailureCode = teams.Length > 1 ? GameplayFactFailureCode.AmbiguousFlagMatch : null;
            }
            if (!alreadyRecorded) await RecordDecisionEventAsync(db, fact, outcome.ObservedAt, ct);
        }
        root.Touch(clock.GetUtcNow());
        await db.SaveChangesAsync(ct); await transaction.CommitAsync(ct); await messages.FlushCommittedMessagesAsync();
    }

    public async Task RecoverAsync(CancellationToken ct)
    {
        await using var db = await contexts.CreateDbContextAsync(ct);
        Guid? after = null;
        while (true)
        {
            var facts = await db.GameplayFacts.AsNoTracking().IgnoreAutoIncludes().Where(value => value.ControlExecution != null
                && value.ControlExecution.FinalizedAt == null && (after == null || value.Id.CompareTo(after.Value) > 0))
                .OrderBy(value => value.Id).Take(64)
                .Select(value => new { value.Id, value.ControlExecution!.ExecutionId, value.ControlExecution.RuntimeInstanceId }).ToArrayAsync(ct);
            foreach (var item in facts) await RecoverOneAsync(item.ExecutionId, item.RuntimeInstanceId, CapabilityExecutionScope.GameplayFact, ct);
            if (facts.Length < 64) break;
            after = facts[^1].Id;
        }
        after = null;
        while (true)
        {
            var tests = await db.RuntimeInstances.AsNoTracking().IgnoreAutoIncludes().Where(value => value.CapabilityTest != null
                && value.CapabilityTest.ControlExecution != null && value.CapabilityTest.ControlExecution.FinalizedAt == null
                && (after == null || value.Id.CompareTo(after.Value) > 0))
                .OrderBy(value => value.Id).Take(64)
                .Select(value => new { value.Id, value.CapabilityTest!.ExecutionId }).ToArrayAsync(ct);
            foreach (var item in tests) await RecoverOneAsync(item.ExecutionId, item.Id, CapabilityExecutionScope.TemplateTest, ct);
            if (tests.Length < 64) break;
            after = tests[^1].Id;
        }
    }

    private async Task RecoverOneAsync(Guid executionId, Guid runtimeId, CapabilityExecutionScope scope, CancellationToken ct)
    {
        await using var db = await contexts.CreateDbContextAsync(ct);
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var root = await LoadAsync(db, executionId, runtimeId, scope, ct);
        if (root is null || root.Execution.FinalizedAt is not null) return;
        if (root.Execution.WorkCompletedAt is null && (clock.GetUtcNow() >= root.Execution.Deadline
            || root.Runtime.State != RuntimeState.Running || root.Test?.CancellationRequested == true))
        {
            root.Execution.WorkCompletedAt = clock.GetUtcNow();
            root.Execution.Failure ??= root.Runtime.State != RuntimeState.Running ? CapabilityExecutionFailure.TargetSuperseded
                : root.Test?.CancellationRequested == true ? CapabilityExecutionFailure.ExecutionInterrupted : CapabilityExecutionFailure.DeadlineExceeded;
            if (root.Fact is { } fact) fact.State = GameplayFactState.PlatformFailed;
            if (root.Test is { } test) test.State = TemplateCapabilityTestState.Finalizing;
            root.Touch(clock.GetUtcNow());
        }
        if (root.Fact is { } failed && root.Execution.Failure is { } failure
            && !failed.CapabilityOutcomes.OfType<ControlOutcome>().Any())
        {
            failed.State = GameplayFactState.PlatformFailed;
            failed.FailureCode = failure == CapabilityExecutionFailure.DeadlineExceeded
                ? GameplayFactFailureCode.ProducerTimeout : GameplayFactFailureCode.ProducerUnavailable;
            failed.CapabilityOutcomes.Add(new ControlOutcome { GameplayFactId = failed.Id, RuntimeInstanceId = runtimeId,
                ObservedAt = clock.GetUtcNow(), Result = ControlResult.Indeterminate,
                ExecutionState = CapabilityExecutionState.PlatformFailed, ExecutionFailure = failure });
            root.Touch(clock.GetUtcNow());
            await RecordDecisionEventAsync(db, failed, clock.GetUtcNow(), ct);
        }
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        if (root.Execution.WorkCompletedAt is not null)
            await messages.PublishAsync(new FinalizeControlObservation(executionId, runtimeId, scope));
        else if (root.Execution.StartedAt is null)
            await messages.PublishToRunnerNodeAsync(new RunControlObservation(executionId, runtimeId, scope, root.Execution.Deadline, root.Execution.RunnerId));
        await messages.FlushCommittedMessagesAsync();
    }

    private static async Task<Guid[]> MatchTeamsAsync(NoCtfDbContext db, Guid challengeId, string identifier, DateTimeOffset at, CancellationToken ct)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(identifier));
        return await db.ChallengeFlags.AsNoTracking().Where(value => value.CompetitionChallengeId == challengeId && value.TeamId != null
            && value.SpecificationKind == SpecificationKind.RuntimeDefinition && value.SpecificationId == challengeId
            && value.FlagSha256 == hash && value.Flag == identifier && value.DeletedAt == null
            && (value.ValidStart == null || value.ValidStart <= at) && (value.ValidUntil == null || value.ValidUntil > at))
            .Select(value => value.TeamId!.Value).Distinct().OrderBy(value => value).Take(2).ToArrayAsync(ct);
    }

    private async Task RecordDecisionEventAsync(NoCtfDbContext db, GameplayFact fact, DateTimeOffset at, CancellationToken ct) =>
        await new NoCTF.Infrastructure.Competitions.Events.CompetitionEventStore(db, messages, clock).RecordAsync(new(
            fact.CompetitionId!.Value, CompetitionEventKind.GameplayFactAdjudicated,
            fact.State == GameplayFactState.PlatformFailed ? CompetitionEventLevel.Error : CompetitionEventLevel.Information,
            CompetitionEventVisibility.Staff, at, TeamId: fact.TeamId, CompetitionChallengeId: fact.CompetitionChallengeId,
            GameplayFactId: fact.Id, GameplayFactKind: fact.Kind, GameplayFactState: fact.State, GameplayFactResult: fact.Result), ct);

    private static async Task<Root?> LoadAsync(NoCtfDbContext db, Guid executionId, Guid runtimeId, CapabilityExecutionScope scope, CancellationToken ct)
    {
        var runtime = await db.RuntimeInstances.AsSplitQuery().SingleOrDefaultAsync(value => value.Id == runtimeId, ct);
        if (runtime is null) return null;
        if (scope == CapabilityExecutionScope.TemplateTest)
            return runtime is { Purpose: RuntimePurpose.TemplateTest, CapabilityTest: { Kind: TemplateCapabilityTestKind.Control, ControlExecution: { } execution } test }
                && test.ExecutionId == executionId ? new(runtime, null, test, execution) : null;
        if (scope != CapabilityExecutionScope.GameplayFact) return null;
        var fact = await db.GameplayFacts.AsSplitQuery().SingleOrDefaultAsync(value => value.Id == executionId
            && value.Kind == GameplayFactKind.ControlObservation && value.ControlExecution != null, ct);
        return fact?.ControlExecution is { } details && details.RuntimeInstanceId == runtimeId
            && fact.CompetitionChallengeId == runtime.CompetitionChallengeId ? new(runtime, fact, null, details) : null;
    }

    private sealed record Root(RuntimeInstance Runtime, GameplayFact? Fact, TemplateCapabilityTest? Test, ControlExecutionDetails Execution)
    {
        public void Touch(DateTimeOffset now)
        {
            if (Fact is { } fact) { fact.UpdatedAt = now; fact.ConcurrencyStamp = Guid.NewGuid(); }
            if (Test is { } test) test.ConcurrencyStamp = Guid.NewGuid();
        }
    }
}
