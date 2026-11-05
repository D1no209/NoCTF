using System.Data;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Challenges.Capabilities;
using NoCTF.Application.Challenges.Flags;
using NoCTF.Application.Challenges.Testing;
using NoCTF.Application.Common;
using NoCTF.Application.Messaging;
using NoCTF.Application.Runtime.Provisioning;
using NoCTF.Domain.Challenges;
using NoCTF.Domain.Challenges.Capabilities;
using NoCTF.Domain.Challenges.Testing;
using NoCTF.Domain.Commands;
using NoCTF.Domain.Runtime;
using NoCTF.GameModes.Registration;
using NoCTF.Infrastructure.Persistence;

namespace NoCTF.Infrastructure.Challenges.Testing;

public sealed class TemplateCapabilityTestStore(NoCtfDbContext db, IChallengeRuntimeTemplateCatalog templates,
    IRuntimePlacementPolicy placement, IPostCommitMessagePublisher messages, TimeProvider clock) : ITemplateCapabilityTestStore
{
    private IQueryable<Challenge> Authorized(ChallengeTestAccess access) => db.Challenges.Where(challenge =>
        challenge.Id == access.ChallengeId && challenge.CatalogScope == ChallengeCatalogScope.QuestionBank && challenge.DeletedAt == null
        && (access.IsAdministrator || challenge.OwnerId == access.ActorUserId || challenge.Managers.Any(manager => manager.UserId == access.ActorUserId)));

    public async Task<OperationResult<ChallengeTestMaterial, TemplateCapabilityTestFailure>> ReadAsync(
        ChallengeTestAccess access, ChallengeCapabilityKind capability, CancellationToken ct)
    {
        await using var snapshot = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var challenge = await Authorized(access).AsNoTracking().AsSplitQuery().SingleOrDefaultAsync(ct);
        if (challenge is null) return Failure<ChallengeTestMaterial>(TemplateCapabilityTestFailure.NotFound);
        if (challenge.Definition is not { } definition || definition.Capabilities.All(value => value.Kind != capability))
            return Failure<ChallengeTestMaterial>(TemplateCapabilityTestFailure.CapabilityMissing);
        if (ChallengeCapabilityValidation.Validate(definition).Any(error => error.Capability is null || error.Capability == capability))
            return Failure<ChallengeTestMaterial>(TemplateCapabilityTestFailure.InvalidConfiguration);
        RuntimeInstance? runtime = null;
        if (access.RuntimeInstanceId is { } runtimeId)
        {
            runtime = await db.RuntimeInstances.AsNoTracking().AsSplitQuery().SingleOrDefaultAsync(value => value.Id == runtimeId
                && value.ChallengeId == challenge.Id && value.Purpose == RuntimePurpose.TemplateTest, ct);
            if (runtime is null) return Failure<ChallengeTestMaterial>(TemplateCapabilityTestFailure.RuntimeScopeMismatch);
            if (runtime.State != RuntimeState.Running) return Failure<ChallengeTestMaterial>(TemplateCapabilityTestFailure.RuntimeNotReady);
        }
        var now = clock.GetUtcNow();
        var flags = capability != ChallengeCapabilityKind.Break ? [] : await db.ChallengeFlags.AsNoTracking()
            .Where(flag => flag.ChallengeId == challenge.Id && flag.DeletedAt == null
                && (flag.ValidStart == null || flag.ValidStart <= now) && (flag.ValidUntil == null || flag.ValidUntil > now)
                && (access.RuntimeInstanceId == null ? flag.Type == ChallengeFlagType.Template
                    : flag.Type == ChallengeFlagType.RuntimeInstance && flag.SpecificationId == access.RuntimeInstanceId))
            .ToArrayAsync(ct);
        return OperationResult<ChallengeTestMaterial, TemplateCapabilityTestFailure>.Success(new(challenge, runtime, flags));
    }

    public async Task<OperationResult<TemplateCapabilityTestAccepted, TemplateCapabilityTestFailure>> StartAsync(
        StartTemplateCapabilityTestCommand command, CancellationToken ct)
    {
        if (command.RequestId == Guid.Empty || !Enum.IsDefined(command.Kind))
            return Failure<TemplateCapabilityTestAccepted>(TemplateCapabilityTestFailure.InvalidInput);
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var challenge = await ChallengeTemplateCriticalSection.AcquireAsync(db, command.Access.ChallengeId, ct);
        if (challenge is null || !await Authorized(command.Access).AnyAsync(ct))
            return Failure<TemplateCapabilityTestAccepted>(TemplateCapabilityTestFailure.NotFound);
        var receiptId = new Guid(SHA256.HashData(Encoding.UTF8.GetBytes(
            $"template-capability-test:{command.Access.ActorUserId:N}:{challenge.Id:N}:{command.RequestId:N}"))[..16]);
        var fingerprint = SHA256.HashData(Encoding.UTF8.GetBytes($"{(short)command.Kind}:{command.Access.RuntimeInstanceId:N}:{command.InputFingerprint}"));
        var receipt = await db.CommandReceipts.AsNoTracking().SingleOrDefaultAsync(value => value.Id == receiptId, ct);
        if (receipt is not null)
        {
            if (receipt.Operation != ReplayOperation.TemplateCapabilityTest || receipt.UserId != command.Access.ActorUserId
                || receipt.ResourceId != challenge.Id || !CryptographicOperations.FixedTimeEquals(receipt.InputFingerprint, fingerprint))
                return Failure<TemplateCapabilityTestAccepted>(TemplateCapabilityTestFailure.RequestConflict);
            return OperationResult<TemplateCapabilityTestAccepted, TemplateCapabilityTestFailure>.Success(new(
                receipt.PrimaryResultId ?? throw new InvalidOperationException("A test receipt requires an execution identity."),
                receipt.SecondaryResultId ?? throw new InvalidOperationException("A test receipt requires a Runtime identity.")));
        }
        if (challenge.Definition is not { } definition
            || command.Kind != TemplateCapabilityTestKind.Check && definition.Fix is null
            || command.Kind != TemplateCapabilityTestKind.Fix && definition.Check is null)
            return Failure<TemplateCapabilityTestAccepted>(TemplateCapabilityTestFailure.CapabilityMissing);
        if (ChallengeCapabilityValidation.Validate(definition).Count != 0)
            return Failure<TemplateCapabilityTestAccepted>(TemplateCapabilityTestFailure.InvalidConfiguration);
        var executionId = Guid.CreateVersion7(command.Now);
        RuntimeInstance runtime;
        if (command.Kind == TemplateCapabilityTestKind.Check)
        {
            var target = await db.RuntimeInstances.AsSplitQuery().SingleOrDefaultAsync(value => value.Id == command.Access.RuntimeInstanceId
                && value.ChallengeId == challenge.Id && value.Purpose == RuntimePurpose.TemplateTest
                && value.TemplateTestUse != TemplateTestUse.CapabilityVerification, ct);
            if (target is null) return Failure<TemplateCapabilityTestAccepted>(TemplateCapabilityTestFailure.RuntimeScopeMismatch);
            if (target.State != RuntimeState.Running || target.ProviderReceipt is null)
                return Failure<TemplateCapabilityTestAccepted>(TemplateCapabilityTestFailure.RuntimeNotReady);
            if (target.CapabilityTest is { IsTerminal: false })
                return Failure<TemplateCapabilityTestAccepted>(TemplateCapabilityTestFailure.Conflict);
            runtime = target;
            if (runtime.CapabilityTest is { } previous)
            {
                if (previous.FileId is { } oldFile) await messages.PublishAsync(new CleanupFile(oldFile));
                db.Remove(previous);
                runtime.CapabilityTest = null;
                await db.SaveChangesAsync(ct);
            }
        }
        else
        {
            var archiveBytes = command.FileId is null ? null : await db.Files.Where(file => file.Id == command.FileId)
                .Select(file => (long?)file.ByteLength).SingleOrDefaultAsync(ct);
            if (archiveBytes is null)
                return Failure<TemplateCapabilityTestAccepted>(TemplateCapabilityTestFailure.InvalidInput);
            if (archiveBytes > definition.Fix!.MaximumUploadBytes
                || archiveBytes > NoCTF.Application.GameplayFacts.PatchUploads.PatchUploadRules.HardMaximumArchiveBytes)
                return Failure<TemplateCapabilityTestAccepted>(TemplateCapabilityTestFailure.ArchiveTooLarge);
            var template = templates.Get(definition);
            if (template is not { RuntimeKind: RuntimeKind.Container })
                return Failure<TemplateCapabilityTestAccepted>(TemplateCapabilityTestFailure.InvalidConfiguration);
            runtime = new TemplateTestRuntimeInstance
            {
                Id = Guid.CreateVersion7(command.Now.AddTicks(1)), ChallengeId = challenge.Id,
                TemplateTestUse = TemplateTestUse.CapabilityVerification, RuntimeKind = RuntimeKind.Container,
                RuntimeProvider = placement.Resolve(RuntimeKind.Container).Provider,
                State = RuntimeState.Queued, CreatedAt = command.Now, AccessMode = RuntimeAccessMode.Direct
            };
            template = template with
            {
                FlagSource = definition.Break is { InjectionCommand: not null }
                    || definition.Break?.FlagStores.Any(store => store.DeliveryKind == FlagDeliveryKind.RunnerCommand) == true
                    ? RuntimeFlagSource.AwdRotation
                    : definition.Break?.EnvironmentTargets.Count > 0 ? RuntimeFlagSource.PerTeam : RuntimeFlagSource.Static
            };
            var flagPlan = ChallengeTestFlagFactory.Create(template, challenge.Id, runtime.Id,
                definition.Break?.HasFlagTemplate == true ? definition.Break.FlagTemplate : null);
            runtime.TestFlagDelivery = flagPlan.Delivery; runtime.TestFlagState = flagPlan.InitialState;
            if (flagPlan.Flag is { } flag)
                db.ChallengeFlags.Add(new RuntimeInstanceChallengeFlag
                {
                    Id = Guid.CreateVersion7(command.Now), ChallengeId = challenge.Id, Flag = flag, FlagSha256 = ManageChallengeFlags.Hash(flag),
                    MatchKind = ChallengeFlagMatchKind.Exact, SpecificationKind = SpecificationKind.RuntimeInstance,
                    SpecificationId = runtime.Id, CreatedAt = command.Now
                });
            db.RuntimeInstances.Add(runtime);
            await messages.PublishAsync(new DispatchRuntime(runtime.Id));
        }
        var deadline = command.Now.AddSeconds(TemplateCapabilityTestBudget.PreparationAndCleanupSeconds + (command.Kind == TemplateCapabilityTestKind.Check ? 0 : definition.Fix!.TimeoutSeconds)
            + (command.Kind == TemplateCapabilityTestKind.Fix ? 0 : definition.Check!.TimeoutSeconds + definition.Check.ReadyTimeoutSeconds));
        runtime.CapabilityTest = new()
        {
            RuntimeInstanceId = runtime.Id, ExecutionId = executionId, ActorUserId = command.Access.ActorUserId,
            RequestId = command.RequestId, Kind = command.Kind, State = TemplateCapabilityTestState.Queued,
            AcceptedAt = command.Now, Deadline = deadline, FileId = command.FileId
        };
        if (runtime.TemplateTestUse == TemplateTestUse.CapabilityVerification) runtime.ExpiresAt = deadline;
        var savedReceipt = CommandReceiptGeneratedCatalog.Create(ReplayOperation.TemplateCapabilityTest);
        savedReceipt.Id = receiptId; savedReceipt.UserId = command.Access.ActorUserId; savedReceipt.ResourceId = challenge.Id;
        savedReceipt.InputFingerprint = fingerprint; savedReceipt.PrimaryResultId = executionId; savedReceipt.SecondaryResultId = runtime.Id;
        savedReceipt.CommittedAt = command.Now;
        db.CommandReceipts.Add(savedReceipt);
        await messages.PublishAsync(new DispatchTemplateCapabilityTest(runtime.Id, executionId));
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        await messages.FlushCommittedMessagesAsync();
        return OperationResult<TemplateCapabilityTestAccepted, TemplateCapabilityTestFailure>.Success(new(executionId, runtime.Id));
    }

    public async Task<TemplateCapabilityTestView?> FindAsync(ChallengeTestAccess access, Guid executionId, CancellationToken ct)
    {
        await using var snapshot = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        if (!await Authorized(access).AnyAsync(ct)) return null;
        var test = await db.RuntimeInstances.AsNoTracking().AsSplitQuery().Where(runtime => runtime.ChallengeId == access.ChallengeId
                && runtime.Purpose == RuntimePurpose.TemplateTest && runtime.CapabilityTest != null && runtime.CapabilityTest.ExecutionId == executionId)
            .Select(runtime => runtime.CapabilityTest).SingleOrDefaultAsync(ct);
        return test is null ? null : new(test.ExecutionId, test.RuntimeInstanceId, test.Kind, test.State, test.AcceptedAt, test.Deadline,
            test.CompletedAt, test.Failure, test.FailureStage, test.FixResult, test.FixExitCode, test.FixExecutionUncertain, test.CheckCompleted,
            test.Probes.OrderBy(probe => probe.Position).Select(probe => new CheckProbeEvidence(probe.Kind, probe.FlagId, probe.Result, probe.Reason)).ToArray());
    }

    private static OperationResult<T, TemplateCapabilityTestFailure> Failure<T>(TemplateCapabilityTestFailure failure) =>
        OperationResult<T, TemplateCapabilityTestFailure>.Failure(failure, $"The capability test was rejected: {failure}.");
}
