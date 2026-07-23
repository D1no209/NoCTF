using System.Security.Cryptography;
using System.Text;
using NoCTF.Application.Common;
using NoCTF.Application.Submissions.Ports;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Submissions;

namespace NoCTF.Application.Submissions.Intake;

public sealed class SubmitFlag(ISubmissionIntakeStore store, ISubmissionAdmissionModePolicy modePolicy)
{
    public async Task<OperationResult<SubmissionAccepted>> ExecuteAsync(
        FlagSubmissionCommand command,
        CancellationToken cancellationToken = default)
    {
        var byteLength = Encoding.UTF8.GetByteCount(command.Flag);
        if (byteLength is < 1 or > 4096 || command.Flag.Contains('\0', StringComparison.Ordinal))
            return OperationResult<SubmissionAccepted>.Failure(
                "flag_invalid", "Flag must contain 1 to 4096 UTF-8 bytes and cannot contain NUL.");

        var snapshot = await store.LoadAdmissionAsync(
            command.CompetitionId, command.CompetitionChallengeId, command.UserId, cancellationToken);
        if (snapshot is null)
            return OperationResult<SubmissionAccepted>.Failure(
                "submission_scope_not_found", "Submission scope was not found.");

        var kind = snapshot.Mode == GameMode.Awdp ? SubmissionKind.Break : SubmissionKind.Flag;
        var rules = modePolicy.GetRules(
            snapshot.Mode, snapshot.CompetitionConfigurationJson, snapshot.ChallengeConfigurationJson);
        var admission = SubmissionAdmissionPolicy.Check(snapshot, kind, rules, command.ReceivedAt);
        if (!admission.Succeeded)
            return OperationResult<SubmissionAccepted>.Failure(admission.ErrorCode!, admission.ErrorMessage!);

        var submissionId = Guid.CreateVersion7(command.ReceivedAt);
        var accepted = await store.TryAcceptFlagAsync(
            new(
                submissionId,
                command.CompetitionId,
                snapshot.TeamId,
                command.CompetitionChallengeId,
                command.UserId,
                kind,
                command.Flag,
                SHA256.HashData(Encoding.UTF8.GetBytes(command.Flag)),
                command.ReceivedAt),
            snapshot,
            rules.MaxFlagAttempts,
            cancellationToken);
        return Map(accepted);
    }

    public async Task<OperationResult<IReadOnlyList<SubmissionAccepted>>> ExecuteBatchAsync(
        Guid competitionId,
        Guid competitionChallengeId,
        Guid userId,
        IReadOnlyList<string> flags,
        DateTimeOffset receivedAt,
        CancellationToken cancellationToken = default)
    {
        if (flags.Count == 0)
            return OperationResult<IReadOnlyList<SubmissionAccepted>>.Failure(
                "flag_invalid", "At least one Flag is required.");
        foreach (var flag in flags)
        {
            var byteLength = Encoding.UTF8.GetByteCount(flag);
            if (byteLength is < 1 or > 4096 || flag.Contains('\0', StringComparison.Ordinal))
                return OperationResult<IReadOnlyList<SubmissionAccepted>>.Failure(
                    "flag_invalid", "Every Flag must contain 1 to 4096 UTF-8 bytes and cannot contain NUL.");
        }

        var snapshot = await store.LoadAdmissionAsync(
            competitionId, competitionChallengeId, userId, cancellationToken);
        if (snapshot is null)
            return OperationResult<IReadOnlyList<SubmissionAccepted>>.Failure(
                "submission_scope_not_found", "Submission scope was not found.");
        if (flags.Count > 1 && snapshot.Mode != GameMode.Awd)
            return OperationResult<IReadOnlyList<SubmissionAccepted>>.Failure(
                "flag_batch_not_supported", "Only AWD accepts a Flag collection.");

        var kind = snapshot.Mode == GameMode.Awdp ? SubmissionKind.Break : SubmissionKind.Flag;
        var rules = modePolicy.GetRules(
            snapshot.Mode, snapshot.CompetitionConfigurationJson, snapshot.ChallengeConfigurationJson);
        var admission = SubmissionAdmissionPolicy.Check(snapshot, kind, rules, receivedAt);
        if (!admission.Succeeded)
            return OperationResult<IReadOnlyList<SubmissionAccepted>>.Failure(
                admission.ErrorCode!, admission.ErrorMessage!);

        var received = flags.Select(flag => new FlagSubmissionReceived(
            Guid.CreateVersion7(receivedAt),
            competitionId,
            snapshot.TeamId,
            competitionChallengeId,
            userId,
            kind,
            flag,
            SHA256.HashData(Encoding.UTF8.GetBytes(flag)),
            receivedAt)).ToArray();
        var results = await store.TryAcceptFlagsAsync(
            received, snapshot, rules.MaxFlagAttempts, cancellationToken);
        if (results.Any(result => result.State != SubmissionAcceptanceState.Created))
        {
            var first = results.First(result => result.State != SubmissionAcceptanceState.Created);
            var mapped = Map(first);
            return OperationResult<IReadOnlyList<SubmissionAccepted>>.Failure(
                mapped.ErrorCode!, mapped.ErrorMessage!);
        }
        return OperationResult<IReadOnlyList<SubmissionAccepted>>.Success(
            results.Select(result => new SubmissionAccepted(
                result.SubmissionId!.Value,
                result.ReceivedAt!.Value)).ToArray());
    }

    private static OperationResult<SubmissionAccepted> Map(SubmissionAcceptanceResult result) => result.State switch
    {
        SubmissionAcceptanceState.Created =>
            OperationResult<SubmissionAccepted>.Success(new(result.SubmissionId!.Value, result.ReceivedAt!.Value)),
        SubmissionAcceptanceState.AttemptsExhausted =>
            OperationResult<SubmissionAccepted>.Failure(
                "attempts_exhausted", "The maximum number of accepted attempts has been reached."),
        _ => OperationResult<SubmissionAccepted>.Failure(
            "submission_concurrency", "The submission could not be accepted because its scope changed.")
    };
}
