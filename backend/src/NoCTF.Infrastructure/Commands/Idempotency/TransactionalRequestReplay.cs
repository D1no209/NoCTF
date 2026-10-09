using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Commands.Idempotency;
using NoCTF.Application.GameplayFacts.Awdp;
using NoCTF.Application.GameplayFacts.Intake;
using NoCTF.Application.GameplayFacts.PatchUploads;
using NoCTF.Application.GameplayFacts.PatchVerification;
using NoCTF.Domain.Commands;
using NoCTF.Infrastructure.Persistence;

namespace NoCTF.Infrastructure.Commands.Idempotency;

/// <summary>The receipt is saved in the same transaction as the business state.</summary>
public sealed class TransactionalRequestReplay(
    NoCtfDbContext db,
    IRequestCommandKey? request = null,
    TimeProvider? clock = null) : IRequestReplay
{
    private Guid receiptId;
    private byte[]? fingerprint;
    private ReplayScope? scope;
    private Type? resultType;

    public bool Replayed { get; private set; }
    public Guid? ActorId => request?.ActorId;

    public async Task<T?> FindAsync<T>(
        ReplayScope commandScope,
        ReplayFingerprintInput input,
        CancellationToken ct) where T : class
    {
        if (request?.Key is not Guid key)
            return null;

        scope = commandScope;
        resultType = typeof(T);
        receiptId = new Guid(SHA256.HashData(Encoding.UTF8.GetBytes(
            $"http-command:{scope.UserId:N}:{(int)scope.Operation}:{scope.CompetitionId:N}:{scope.ResourceId:N}:{key:N}"))
            .AsSpan(0, 16));
        fingerprint = SHA256.HashData(SerializeFingerprint(input));

        var receipt = await db.CommandReceipts.AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == receiptId, ct);
        if (receipt is null)
            return null;
        if (receipt.Operation != scope.Operation
            || receipt.UserId != scope.UserId
            || !CryptographicOperations.FixedTimeEquals(receipt.InputFingerprint, fingerprint))
        {
            throw new RequestReplayConflictException();
        }

        Replayed = true;
        var result = ReadResult(receipt);
        return result as T
            ?? throw new InvalidOperationException(
                $"Receipt operation {receipt.Operation} cannot produce {typeof(T).Name}.");
    }

    private static byte[] SerializeFingerprint(ReplayFingerprintInput input) => input switch
    {
        EmptyReplayFingerprint value => JsonSerializer.SerializeToUtf8Bytes(value,
            ReplayFingerprintJsonContext.Default.EmptyReplayFingerprint),
        RuntimeReplayFingerprint value => JsonSerializer.SerializeToUtf8Bytes(value,
            ReplayFingerprintJsonContext.Default.RuntimeReplayFingerprint),
        ScopedRuntimeReplayFingerprint value => JsonSerializer.SerializeToUtf8Bytes(value,
            ReplayFingerprintJsonContext.Default.ScopedRuntimeReplayFingerprint),
        FlagReplayFingerprint value => JsonSerializer.SerializeToUtf8Bytes(value,
            ReplayFingerprintJsonContext.Default.FlagReplayFingerprint),
        ManualAdjustmentReplayFingerprint value => JsonSerializer.SerializeToUtf8Bytes(value,
            ReplayFingerprintJsonContext.Default.ManualAdjustmentReplayFingerprint),
        AdminRuntimeReplayFingerprint value => JsonSerializer.SerializeToUtf8Bytes(value,
            ReplayFingerprintJsonContext.Default.AdminRuntimeReplayFingerprint),
        PatchUploadReplayFingerprint value => JsonSerializer.SerializeToUtf8Bytes(value,
            ReplayFingerprintJsonContext.Default.PatchUploadReplayFingerprint),
        _ => throw new ArgumentOutOfRangeException(nameof(input), input,
            "Unsupported command fingerprint input.")
    };

    public void Store<T>(T response) where T : class
    {
        if (request?.Key is null)
            return;
        if (scope is null
            || fingerprint is null
            || resultType != typeof(T)
            || db.Database.IsRelational() && db.Database.CurrentTransaction is null)
        {
            throw new InvalidOperationException(
                "A command receipt must match the initialized request and share its business transaction.");
        }

        var receipt = CommandReceiptGeneratedCatalog.Create(scope.Operation);
        receipt.Id = receiptId;
        receipt.UserId = scope.UserId;
        receipt.CompetitionId = scope.CompetitionId;
        receipt.ResourceId = scope.ResourceId;
        receipt.InputFingerprint = fingerprint;
        receipt.CommittedAt = (clock ?? TimeProvider.System).GetUtcNow();
        WriteResult(receipt, response);
        db.CommandReceipts.Add(receipt);
    }

    private static object ReadResult(CommandReceipt receipt) => receipt.Operation switch
    {
        ReplayOperation.FlagSubmission => receipt.GameplayFactResults
            .OrderBy(result => result.Position)
            .Select(result => new GameplayFactAcceptanceResult(
                (GameplayFactAcceptanceState)result.State,
                result.GameplayFactId,
                result.OccurredAt))
            .ToArray(),
        ReplayOperation.ManualAdjustment => new GameplayFactAcceptanceResult(
            (GameplayFactAcceptanceState)receipt.ResultState,
            receipt.PrimaryResultId,
            receipt.ResultOccurredAt),
        ReplayOperation.PatchUpload => new AcceptedAwdpFix(
            receipt.PrimaryResultId
                ?? throw MissingResult(receipt, nameof(receipt.PrimaryResultId)),
            receipt.SecondaryResultId
                ?? throw MissingResult(receipt, nameof(receipt.SecondaryResultId)),
            receipt.GameplayFactState
                ?? throw MissingResult(receipt, nameof(receipt.GameplayFactState))),
        ReplayOperation.RuntimeMutation
            or ReplayOperation.AdminRuntimeMutation
            or ReplayOperation.TemplateTestRuntimeMutation => new RuntimeCommandReceipt(
                receipt.PrimaryResultId
                    ?? throw MissingResult(receipt, nameof(receipt.PrimaryResultId))),
        ReplayOperation.AwdpDefenseTarget => new AwdpDefenseTargetRequestResult(
            (AwdpDefenseTargetRequestState)receipt.ResultState,
            receipt.PrimaryResultId,
            receipt.RuntimeState),
        ReplayOperation.PatchVerificationTarget => new PatchVerificationTargetRequestResult(
            (PatchVerificationTargetRequestState)receipt.ResultState,
            receipt.PrimaryResultId,
            receipt.RuntimeState),
        _ => throw new ArgumentOutOfRangeException(
            nameof(receipt),
            receipt.Operation,
            "Unsupported command receipt operation.")
    };

    private static void WriteResult<T>(CommandReceipt receipt, T response) where T : class
    {
        switch (receipt.Operation, response)
        {
            case (ReplayOperation.FlagSubmission, GameplayFactAcceptanceResult[] results):
                receipt.GameplayFactResults.AddRange(results.Select((result, position) =>
                    new CommandReceiptGameplayFactResult
                    {
                        CommandReceiptId = receipt.Id,
                        Position = position,
                        State = (short)result.State,
                        GameplayFactId = result.GameplayFactId,
                        OccurredAt = result.OccurredAt
                    }));
                return;
            case (ReplayOperation.ManualAdjustment, GameplayFactAcceptanceResult result):
                receipt.ResultState = (short)result.State;
                receipt.PrimaryResultId = result.GameplayFactId;
                receipt.ResultOccurredAt = result.OccurredAt;
                return;
            case (ReplayOperation.PatchUpload, AcceptedAwdpFix result):
                receipt.PrimaryResultId = result.PatchUploadId;
                receipt.SecondaryResultId = result.GameplayFactId;
                receipt.GameplayFactState = result.State;
                return;
            case (ReplayOperation.RuntimeMutation
                or ReplayOperation.AdminRuntimeMutation
                or ReplayOperation.TemplateTestRuntimeMutation, RuntimeCommandReceipt result):
                receipt.PrimaryResultId = result.RuntimeInstanceId;
                return;
            case (ReplayOperation.AwdpDefenseTarget, AwdpDefenseTargetRequestResult result):
                receipt.ResultState = (short)result.State;
                receipt.PrimaryResultId = result.RuntimeInstanceId;
                receipt.RuntimeState = result.RuntimeState;
                return;
            case (ReplayOperation.PatchVerificationTarget, PatchVerificationTargetRequestResult result):
                receipt.ResultState = (short)result.State;
                receipt.PrimaryResultId = result.RuntimeInstanceId;
                receipt.RuntimeState = result.RuntimeState;
                return;
            default:
                throw new InvalidOperationException(
                    $"Receipt operation {receipt.Operation} cannot store {typeof(T).Name}.");
        }
    }

    private static InvalidOperationException MissingResult(
        CommandReceipt receipt,
        string property) => new(
        $"Committed {receipt.Operation} receipt {receipt.Id} has no {property}.");
}
