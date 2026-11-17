using NoCTF.Application.Common;

namespace NoCTF.Application.Competitions.Credentials;

public sealed record CompetitionCredentialImportRow(
    int RowNumber,
    string UserName,
    string? Email,
    string TeamName,
    bool Captain,
    string? TrackKey,
    string? FullName = null,
    string? StudentNumber = null,
    string? School = null);

public sealed record CompetitionCredentialImportCommand(
    Guid CompetitionId,
    Guid ActorUserId,
    string SourceFileName,
    string SourceSha256,
    IReadOnlyList<CompetitionCredentialImportRow> Rows,
    DateTimeOffset Now);

public enum CompetitionCredentialImportFailure
{
    None,
    CompetitionNotFound,
    CompetitionDeleted,
    CompetitionFinished,
    Forbidden,
    EmptyFile,
    InvalidRows,
    DuplicateUserName,
    DuplicateEmail,
    DuplicateTeam,
    TeamMemberLimitExceeded,
    InvalidTrack,
    UserNameConflict,
    EmailConflict,
    TransactionConflict,
    CredentialNotFound
}

public enum CompetitionCredentialImportIssueCode
{
    MissingColumn,
    MissingValue,
    InvalidBoolean,
    InvalidTrackKey,
    DuplicateUserName,
    DuplicateEmail,
    DuplicateTeam,
    TeamCaptainConflict,
    TeamMemberLimitExceeded,
    InvalidValue,
    ValueTooLong,
    DuplicateColumn,
    UnsupportedFile,
    InvalidFile,
    ExistingUserName,
    ExistingEmail,
    ExistingTeam
}

public sealed record CompetitionCredentialImportIssue(
    int RowNumber,
    CompetitionCredentialImportIssueCode Code,
    string Message);

public sealed record CompetitionCredentialImportResult(
    Guid? BatchId,
    CompetitionCredentialImportFailure Failure,
    IReadOnlyList<CompetitionCredentialImportIssue> Issues,
    int CreatedUsers,
    int CreatedTeams,
    int CreatedCredentials);

public sealed record CompetitionCredentialDownloadRow(
    Guid UserId,
    string UserName,
    string? Email,
    string TeamName,
    bool Captain,
    string TrackKey,
    string Password,
    string? FullName = null,
    string? StudentNumber = null,
    string? School = null);

public sealed record CompetitionCredentialBatchView(
    Guid BatchId,
    Guid CompetitionId,
    DateTimeOffset CreatedAt,
    string SourceFileName,
    int PasswordVersion,
    IReadOnlyList<CompetitionCredentialDownloadRow> Rows);

public sealed record CompetitionCredentialBatchSummary(
    Guid BatchId,
    Guid CompetitionId,
    DateTimeOffset CreatedAt,
    string SourceFileName,
    int CredentialCount,
    int PasswordVersion);

public sealed record CompetitionCredentialBatchRequest(
    Guid CompetitionId,
    Guid BatchId,
    Guid ActorUserId,
    DateTimeOffset Now);

public sealed record CompetitionCredentialUserRequest(
    Guid CompetitionId,
    Guid UserId,
    Guid ActorUserId,
    DateTimeOffset Now);

public sealed record CompetitionCredentialMutationResult(
    CompetitionCredentialImportFailure Failure,
    string? Password = null);

public interface ICompetitionCredentialStore
{
    Task<CompetitionCredentialImportResult> ImportAsync(
        CompetitionCredentialImportCommand command,
        CancellationToken cancellationToken);

    Task<CompetitionCredentialBatchView?> ReadBatchAsync(
        CompetitionCredentialBatchRequest request,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<CompetitionCredentialBatchSummary>> ListBatchesAsync(
        Guid competitionId,
        Guid actorUserId,
        CancellationToken cancellationToken);

    Task<CompetitionCredentialMutationResult> RotateBatchAsync(
        CompetitionCredentialBatchRequest request,
        CancellationToken cancellationToken);

    Task<CompetitionCredentialMutationResult> RotateUserAsync(
        CompetitionCredentialUserRequest request,
        CancellationToken cancellationToken);
}

public sealed class ImportCompetitionCredentials(ICompetitionCredentialStore store)
{
    public Task<CompetitionCredentialImportResult> ExecuteAsync(
        CompetitionCredentialImportCommand command,
        CancellationToken cancellationToken = default) =>
        store.ImportAsync(command, cancellationToken);
}

public sealed class ReadCompetitionCredentialBatch(ICompetitionCredentialStore store)
{
    public Task<CompetitionCredentialBatchView?> ExecuteAsync(
        CompetitionCredentialBatchRequest request,
        CancellationToken cancellationToken = default) =>
        store.ReadBatchAsync(request, cancellationToken);
}

public sealed class ListCompetitionCredentialBatches(ICompetitionCredentialStore store)
{
    public Task<IReadOnlyList<CompetitionCredentialBatchSummary>> ExecuteAsync(
        Guid competitionId,
        Guid actorUserId,
        CancellationToken cancellationToken = default) =>
        store.ListBatchesAsync(competitionId, actorUserId, cancellationToken);
}

public sealed class RotateCompetitionCredentialBatch(ICompetitionCredentialStore store)
{
    public Task<CompetitionCredentialMutationResult> ExecuteAsync(
        CompetitionCredentialBatchRequest request,
        CancellationToken cancellationToken = default) =>
        store.RotateBatchAsync(request, cancellationToken);
}

public sealed class RotateCompetitionCredentialUser(ICompetitionCredentialStore store)
{
    public Task<CompetitionCredentialMutationResult> ExecuteAsync(
        CompetitionCredentialUserRequest request,
        CancellationToken cancellationToken = default) =>
        store.RotateUserAsync(request, cancellationToken);
}
