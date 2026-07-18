using NoCTF.Application.Common;
using NoCTF.Application.Submissions.Intake;
using NoCTF.Application.Submissions.Ports;

namespace NoCTF.Application.Storage;

public sealed record FixUploadCreated(FixUploadGrant Grant);

public sealed class CreateFixUpload(
    ISubmissionIntakeStore admissionStore,
    IFixUploadSessionStore sessions,
    Func<DateTimeOffset>? clock = null)
{
    private static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(15);

    public Task<OperationResult<FixUploadCreated>> ExecuteAsync(
        CreateFixUploadCommand command,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(command.FileName))
            return Task.FromResult(OperationResult<FixUploadCreated>.Failure("file_name_required", "A file name is required."));
        if (command.Length <= 0)
            return Task.FromResult(OperationResult<FixUploadCreated>.Failure("file_length_invalid", "The archive must not be empty."));
        if (string.IsNullOrWhiteSpace(command.Sha256)
            || command.Sha256.Length != 64
            || !IsSha256(command.Sha256))
            return Task.FromResult(OperationResult<FixUploadCreated>.Failure("sha256_required", "A SHA-256 checksum is required."));

        return CreateAsync(command, cancellationToken);
    }

    private async Task<OperationResult<FixUploadCreated>> CreateAsync(
        CreateFixUploadCommand command,
        CancellationToken cancellationToken)
    {
        var now = (clock ?? (() => DateTimeOffset.UtcNow))();
        var admission = await admissionStore.LoadAdmissionAsync(
            command.CompetitionId,
            command.TeamId,
            command.ChallengeId,
            command.UserId,
            cancellationToken);
        if (admission is null)
            return OperationResult<FixUploadCreated>.Failure("upload_scope_not_found", "The upload scope was not found.");
        var decision = SubmissionAdmissionPolicy.Check(admission, now);
        if (!decision.Succeeded)
            return OperationResult<FixUploadCreated>.Failure(decision.ErrorCode!, decision.ErrorMessage!);

        var grant = await sessions.CreateAsync(command with
        {
            RequestedAt = now
        }, cancellationToken);
        return grant is null
            ? OperationResult<FixUploadCreated>.Failure("upload_scope_not_found", "The upload scope was not found.")
            : OperationResult<FixUploadCreated>.Success(new(grant));
    }

    private static bool IsSha256(string value)
    {
        try
        {
            return Convert.FromHexString(value).Length == 32;
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
