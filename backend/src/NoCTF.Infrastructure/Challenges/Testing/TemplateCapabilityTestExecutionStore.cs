using System.Data;
using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Challenges.Capabilities;
using NoCTF.Application.Challenges.Testing;
using NoCTF.Application.GameplayFacts.Processing;
using NoCTF.Application.Messaging;
using NoCTF.Application.Runtime.Provisioning;
using NoCTF.Domain.Challenges;
using NoCTF.Domain.Challenges.Capabilities;
using NoCTF.Domain.Challenges.Testing;
using NoCTF.Domain.Runtime;
using NoCTF.Domain.Storage;
using NoCTF.Infrastructure.Persistence;
using NoCTF.Infrastructure.Authentication;
using NoCTF.Infrastructure.Challenges.Capabilities;

namespace NoCTF.Infrastructure.Challenges.Testing;

public sealed record TemplateCapabilityTestWork(Guid ExecutionId, Guid RuntimeInstanceId, TemplateCapabilityTestKind Kind,
    DateTimeOffset Deadline, FixCapabilityDefinition? Fix, CheckCapabilityDefinition? Check, ContainerRuntimeReceiptData Receipt,
    StoredFile? Archive, IReadOnlyList<RequiredCheckEvidence> Requirements, IReadOnlyList<ChallengeFlag> Flags,
    IReadOnlyList<CheckFlagContextEvidence> FlagContexts);

public sealed class TemplateCapabilityTestExecutionStore(IDbContextFactory<NoCtfDbContext> contexts,
    IPostCommitMessagePublisher messages, TimeProvider clock, PlatformSecretProtector? secrets = null) : ITemplateCapabilityCheckResultStore
{
    public async Task DispatchAsync(DispatchTemplateCapabilityTest command, CancellationToken ct)
    {
        await using var db = await contexts.CreateDbContextAsync(ct);
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var runtime = await db.RuntimeInstances.AsSplitQuery().SingleOrDefaultAsync(value => value.Id == command.RuntimeInstanceId, ct);
        if (runtime is not { Purpose: RuntimePurpose.TemplateTest, CapabilityTest: { } test }
            || test.ExecutionId != command.ExecutionId || test.IsTerminal) return;
        var now = clock.GetUtcNow();
        if (test.State == TemplateCapabilityTestState.Queued && runtime.TestFlagState is RuntimeTestFlagState.Failed or RuntimeTestFlagState.Canceled)
        {
            test.Fail(CapabilityExecutionFailure.InvalidEvidence);
            test.State = TemplateCapabilityTestState.Finalizing;
        }
        if (test.State != TemplateCapabilityTestState.Finalizing && (now >= test.Deadline
            || runtime.State is RuntimeState.Failed or RuntimeState.Stopped or RuntimeState.Stopping))
        {
            if (!test.CancellationRequested)
                test.Fail(now >= test.Deadline ? CapabilityExecutionFailure.DeadlineExceeded : CapabilityExecutionFailure.RuntimeUnavailable);
            test.State = TemplateCapabilityTestState.Finalizing;
        }
        if (test.State == TemplateCapabilityTestState.Finalizing)
        {
            if (runtime.FailureCode == RuntimeFailureCode.CleanupFailed) test.Fail(CapabilityExecutionFailure.ResourceCleanupFailed);
            var independent = runtime.TemplateTestUse == TemplateTestUse.CapabilityVerification;
            var resourcesRemain = independent
                ? runtime.State != RuntimeState.Stopped || runtime.CapacityAllocations.Items.Count != 0
                : runtime.CapacityAllocations.Items.Any(allocation => allocation.Identity.Kind == RuntimeWorkloadKind.TemplateChecker
                    && allocation.Identity.OperationId == test.ExecutionId);
            if (independent && runtime.State != RuntimeState.Stopped)
            {
                if (runtime.RunnerId is null && runtime.ProviderReceipt is null && runtime.CapacityAllocations.Items.Count == 0)
                { runtime.State = RuntimeState.Stopped; runtime.StoppedAt = now; resourcesRemain = false; }
                else
                {
                    runtime.State = RuntimeState.Stopping;
                    await messages.PublishAsync(new StopRuntime(runtime.Id));
                }
            }
            if (!resourcesRemain)
            {
                test.State = test.CancellationRequested ? TemplateCapabilityTestState.Canceled
                    : test.Failure is null ? TemplateCapabilityTestState.Completed : TemplateCapabilityTestState.PlatformFailed;
                test.CompletedAt = now;
                if (test.FileId is { } fileId)
                {
                    test.FileId = null;
                    await messages.PublishAsync(new CleanupFile(fileId));
                }
            }
        }
        else if (test.State == TemplateCapabilityTestState.Queued && runtime.TestFlagState != RuntimeTestFlagState.Pending
            && runtime is { State: RuntimeState.Running, RunnerId: { } runnerId })
            await messages.PublishToRunnerNodeAsync(new RunTemplateCapabilityTest(runtime.Id, test.ExecutionId, runnerId));
        test.ConcurrencyStamp = Guid.NewGuid();
        await db.SaveChangesAsync(ct); await transaction.CommitAsync(ct); await messages.FlushCommittedMessagesAsync();
    }

    public async Task RecoverAsync(CancellationToken ct)
    {
        await using var db = await contexts.CreateDbContextAsync(ct);
        var now = clock.GetUtcNow();
        var tests = await db.RuntimeInstances.AsNoTracking().IgnoreAutoIncludes()
            .Where(runtime => runtime.CapabilityTest != null && (runtime.CapabilityTest.State == TemplateCapabilityTestState.Queued
                || runtime.CapabilityTest.State == TemplateCapabilityTestState.Finalizing
                || (runtime.CapabilityTest.State == TemplateCapabilityTestState.Applying || runtime.CapabilityTest.State == TemplateCapabilityTestState.Checking)
                    && (runtime.CapabilityTest.Deadline <= now || runtime.State == RuntimeState.Stopping
                        || runtime.State == RuntimeState.Stopped || runtime.State == RuntimeState.Failed)))
            .OrderBy(runtime => runtime.CapabilityTest!.Deadline).ThenBy(runtime => runtime.Id).Take(64)
            .Select(runtime => new { RuntimeInstanceId = runtime.Id, runtime.CapabilityTest!.ExecutionId }).ToArrayAsync(ct);
        foreach (var test in tests) await messages.PublishAsync(new DispatchTemplateCapabilityTest(test.RuntimeInstanceId, test.ExecutionId));
        await messages.FlushCommittedMessagesAsync();
    }

    public async Task<TemplateCapabilityTestWork?> ClaimAsync(RunTemplateCapabilityTest command, CancellationToken ct)
    {
        await using var db = await contexts.CreateDbContextAsync(ct);
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var runtime = await db.RuntimeInstances.AsSplitQuery().SingleOrDefaultAsync(value => value.Id == command.RuntimeInstanceId, ct);
        if (runtime is not { Purpose: RuntimePurpose.TemplateTest, State: RuntimeState.Running, CapabilityTest: { } test }
            || runtime.RunnerId != command.RunnerId || test.ExecutionId != command.ExecutionId || test.IsTerminal) return null;
        if (test.State == TemplateCapabilityTestState.Finalizing) return null;
        async Task<TemplateCapabilityTestWork?> RejectAsync(CapabilityExecutionFailure? failure)
        {
            if (failure is { } reason) test.Fail(reason);
            test.State = TemplateCapabilityTestState.Finalizing;
            test.ConcurrencyStamp = Guid.NewGuid();
            await messages.PublishAsync(new DispatchTemplateCapabilityTest(runtime.Id, test.ExecutionId));
            await db.SaveChangesAsync(ct); await transaction.CommitAsync(ct); await messages.FlushCommittedMessagesAsync();
            return null;
        }
        if (test.State != TemplateCapabilityTestState.Queued || clock.GetUtcNow() >= test.Deadline)
        {
            if (test.State == TemplateCapabilityTestState.Applying && test.MutationStartedAt is not null && test.FixResult is null)
                test.FixExecutionUncertain = true;
            return await RejectAsync(test.CheckCompleted ? null : clock.GetUtcNow() >= test.Deadline
                ? CapabilityExecutionFailure.DeadlineExceeded : CapabilityExecutionFailure.ExecutionInterrupted);
        }
        if (runtime.TestFlagState == RuntimeTestFlagState.Pending) return null;
        if (runtime.TestFlagState is RuntimeTestFlagState.Failed or RuntimeTestFlagState.Canceled)
            return await RejectAsync(CapabilityExecutionFailure.InvalidEvidence);
        var challenge = await db.Challenges.AsNoTracking().AsSplitQuery().SingleOrDefaultAsync(value => value.Id == runtime.ChallengeId
            && value.CatalogScope == ChallengeCatalogScope.QuestionBank && value.DeletedAt == null, ct);
        var definition = challenge?.Definition;
        var receipt = runtime.ProviderReceipt?.ToData() as ContainerRuntimeReceiptData;
        if (definition is null || receipt is null) return await RejectAsync(CapabilityExecutionFailure.RuntimeUnavailable);
        var flags = await db.ChallengeFlags.AsNoTracking().Where(flag => flag.ChallengeId == challenge!.Id
            && flag.SpecificationKind == SpecificationKind.RuntimeInstance && flag.SpecificationId == runtime.Id && flag.DeletedAt == null).ToArrayAsync(ct);
        if (test.Kind != TemplateCapabilityTestKind.Fix)
        {
            if (definition.Check is not { } check) return await RejectAsync(CapabilityExecutionFailure.InvalidInput);
            var kinds = test.Kind == TemplateCapabilityTestKind.FixAndCheck
                ? check.Requirements.Select(item => item.Kind).Where(kind => kind is CheckItemKind.Connection or CheckItemKind.Functionality or CheckItemKind.Vulnerability)
                    .Append(CheckItemKind.Functionality).Distinct().ToArray()
                : check.Requirements.Select(item => item.Kind).ToArray();
            foreach (var kind in kinds)
            {
                if (kind is CheckItemKind.FlagWrite or CheckItemKind.CurrentFlagRead or CheckItemKind.HistoricalFlagRead)
                {
                    var relevant = flags.Where(flag => kind == CheckItemKind.HistoricalFlagRead ? flag.ValidUntil <= test.AcceptedAt
                        : flag.ValidUntil is null || flag.ValidUntil > test.AcceptedAt).ToArray();
                    foreach (var flag in relevant) test.Requirements.Add(new() { RuntimeInstanceId = runtime.Id, Position = test.Requirements.Count, Kind = kind, FlagId = flag.Id });
                }
                else test.Requirements.Add(new() { RuntimeInstanceId = runtime.Id, Position = test.Requirements.Count, Kind = kind });
            }
            if (test.Requirements.Count == 0) return await RejectAsync(CapabilityExecutionFailure.MissingEvidence);
        }
        test.State = test.Kind == TemplateCapabilityTestKind.Check ? TemplateCapabilityTestState.Checking : TemplateCapabilityTestState.Applying;
        if (test.Kind == TemplateCapabilityTestKind.Check) test.CheckStartedAt = clock.GetUtcNow();
        test.ConcurrencyStamp = Guid.NewGuid();
        var archive = test.FileId is { } fileId ? await db.Files.AsNoTracking().SingleAsync(file => file.Id == fileId, ct) : null;
        await db.SaveChangesAsync(ct); await transaction.CommitAsync(ct);
        return new(test.ExecutionId, runtime.Id, test.Kind, test.Deadline, definition.Fix, definition.Check, receipt, archive,
            test.Requirements.OrderBy(item => item.Position).Select(item => new RequiredCheckEvidence(item.Kind, item.FlagId)).ToArray(), flags,
            flags.Select(flag => new CheckFlagContextEvidence(flag.Id, flag.ProtectedReadContext is null ? null
                : (secrets ?? throw new InvalidOperationException("Flag context protection is unavailable.")).Unprotect(
                    flag.ProtectedReadContext, PlatformSecretPurpose.FlagReadContext, challenge!.Id, flag.Id), flag.PublicLocator)).ToArray());
    }

    public async Task<bool> RecordFixAsync(FixExecutionResult result, string runnerId, CancellationToken ct)
    {
        await using var db = await contexts.CreateDbContextAsync(ct);
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var runtime = await db.RuntimeInstances.AsSplitQuery().SingleOrDefaultAsync(value => value.Id == result.RuntimeInstanceId, ct);
        if (result.Scope != CapabilityExecutionScope.TemplateTest
            || runtime is not { State: RuntimeState.Running, CapabilityTest: { State: TemplateCapabilityTestState.Applying } test }
            || runtime.RunnerId != runnerId
            || test.ExecutionId != result.ExecutionId || clock.GetUtcNow() >= test.Deadline) return false;
        test.FixResult = result.Result; test.FixExitCode = result.ExitCode; test.FixExecutionUncertain = result.ExecutionUncertain;
        if (result.ExecutionFailure is { } failure) test.Fail(failure);
        test.State = result.Result == FixResult.Applied && result.ExecutionFailure is null && test.Kind == TemplateCapabilityTestKind.FixAndCheck
            ? TemplateCapabilityTestState.Checking : TemplateCapabilityTestState.Finalizing;
        if (test.State == TemplateCapabilityTestState.Checking) test.CheckStartedAt = clock.GetUtcNow();
        test.ConcurrencyStamp = Guid.NewGuid();
        await db.SaveChangesAsync(ct); await transaction.CommitAsync(ct);
        return test.State == TemplateCapabilityTestState.Checking;
    }

    public async Task CompleteWorkAsync(Guid runtimeId, Guid executionId, string runnerId, CapabilityExecutionFailure? failure, CancellationToken ct)
    {
        await using var db = await contexts.CreateDbContextAsync(ct);
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var test = await db.RuntimeInstances.Where(runtime => runtime.Id == runtimeId && runtime.RunnerId == runnerId
            && runtime.Purpose == RuntimePurpose.TemplateTest && runtime.CapabilityTest != null && runtime.CapabilityTest.ExecutionId == executionId)
            .Select(runtime => runtime.CapabilityTest).SingleOrDefaultAsync(ct);
        if (test is null || test.IsTerminal) return;
        if (test.State == TemplateCapabilityTestState.Applying && test.MutationStartedAt is not null && test.FixResult is null)
            test.FixExecutionUncertain = true;
        var reason = failure ?? (test.State == TemplateCapabilityTestState.Checking && !test.CheckCompleted ? CapabilityExecutionFailure.MissingEvidence : null);
        if (reason is { } value) test.Fail(value);
        test.State = TemplateCapabilityTestState.Finalizing; test.ConcurrencyStamp = Guid.NewGuid();
        await messages.PublishAsync(new DispatchTemplateCapabilityTest(runtimeId, executionId));
        await db.SaveChangesAsync(ct); await transaction.CommitAsync(ct); await messages.FlushCommittedMessagesAsync();
    }

    public async Task<InternalResultDisposition> RecordCheckAsync(Guid runtimeId, Guid executionId, string runnerId,
        IReadOnlyList<CheckProbeEvidence> probes, IReadOnlyList<CheckFlagContextEvidence> flagContexts, DateTimeOffset observedAt, CancellationToken ct)
    {
        await using var db = await contexts.CreateDbContextAsync(ct);
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var runtime = await db.RuntimeInstances.AsSplitQuery().SingleOrDefaultAsync(value => value.Id == runtimeId, ct);
        if (runtime is not { Purpose: RuntimePurpose.TemplateTest, CapabilityTest: { } test } || test.ExecutionId != executionId)
            return InternalResultDisposition.NotFound;
        if (runtime.State != RuntimeState.Running || runtime.RunnerId != runnerId || clock.GetUtcNow() > test.Deadline)
            return InternalResultDisposition.Superseded;
        if (test.CheckCompleted)
            return test.CheckObservedAt == observedAt && test.Probes.OrderBy(value => value.Position)
                .Select(value => new CheckProbeEvidence(value.Kind, value.FlagId, value.Result, value.Reason)).OrderBy(value => value.Kind).ThenBy(value => value.FlagId)
                    .SequenceEqual(probes.OrderBy(value => value.Kind).ThenBy(value => value.FlagId))
                && test.FlagContexts.Count == flagContexts.Count && test.FlagContexts.All(context => flagContexts.Any(report => report.FlagId == context.FlagId
                    && report.PublicLocator == context.PublicLocator && string.Equals(report.PrivateReadContext,
                        CheckFlagContextProtection.Reveal(context.ProtectedReadContext, executionId, context.FlagId, secrets), StringComparison.Ordinal)))
                ? InternalResultDisposition.Duplicate : InternalResultDisposition.Conflict;
        if (test.State != TemplateCapabilityTestState.Checking || test.CheckStartedAt is null)
            return InternalResultDisposition.Superseded;
        if (CheckFlagContextValidation.Validate(test.Requirements.Where(value => value.FlagId.HasValue)
            .Select(value => value.FlagId!.Value).ToHashSet(), flagContexts) is { } contextFailure)
        {
            test.Fail(contextFailure);
            await db.SaveChangesAsync(ct); await transaction.CommitAsync(ct);
            return InternalResultDisposition.Conflict;
        }
        var result = CheckEvidenceValidation.Evaluate(new(executionId, runtimeId,
            test.Requirements.Select(value => new RequiredCheckEvidence(value.Kind, value.FlagId)).ToArray(), probes,
            test.CheckStartedAt.Value, test.Deadline, observedAt, clock.GetUtcNow(), CapabilityExecutionScope.TemplateTest));
        if (!result.Succeeded)
        {
            test.Fail(result.FailureCode!.Value);
            await db.SaveChangesAsync(ct); await transaction.CommitAsync(ct);
            return InternalResultDisposition.Conflict;
        }
        test.Probes = probes.Select((probe, position) => new TemplateCheckProbe
        {
            RuntimeInstanceId = runtimeId, Position = position, Kind = probe.Kind, FlagId = probe.FlagId, Result = probe.Result, Reason = probe.Reason
        }).ToList();
        var ids = flagContexts.Select(value => value.FlagId).ToArray();
        var flags = await db.ChallengeFlags.Where(flag => ids.Contains(flag.Id) && flag.ChallengeId == runtime.ChallengeId
            && flag.SpecificationKind == SpecificationKind.RuntimeInstance && flag.SpecificationId == runtimeId && flag.DeletedAt == null)
            .ToDictionaryAsync(flag => flag.Id, ct);
        if (flags.Count != ids.Length) return InternalResultDisposition.Conflict;
        foreach (var context in flagContexts)
        {
            test.FlagContexts.Add(new()
            {
                RuntimeInstanceId = runtimeId, FlagId = context.FlagId, PublicLocator = context.PublicLocator,
                ProtectedReadContext = CheckFlagContextProtection.Protect(context.PrivateReadContext, executionId, context.FlagId, secrets)
            });
            if (!test.Requirements.Any(value => value.Kind == CheckItemKind.FlagWrite && value.FlagId == context.FlagId)) continue;
            if (context.PrivateReadContext is { } privateContext)
                flags[context.FlagId].ProtectedReadContext = (secrets ?? throw new InvalidOperationException("Flag context protection is unavailable."))
                    .Protect(privateContext, PlatformSecretPurpose.FlagReadContext,
                        runtime.ChallengeId ?? throw new InvalidOperationException("A template Check requires its challenge scope."), context.FlagId);
            if (context.PublicLocator is { } publicLocator) flags[context.FlagId].PublicLocator = publicLocator;
        }
        test.CheckCompleted = true; test.CheckObservedAt = observedAt;
        if (result.Value!.ExecutionFailure is { } failure) test.Fail(failure);
        test.ConcurrencyStamp = Guid.NewGuid();
        await db.SaveChangesAsync(ct); await transaction.CommitAsync(ct);
        return InternalResultDisposition.Applied;
    }
}
