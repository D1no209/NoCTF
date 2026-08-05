using NoCTF.Application.Administration;
using NoCTF.Application.Administration.UserAccounts;
using NoCTF.Application.Administration.PlatformLogs;
using NoCTF.Domain.Identity;

namespace NoCTF.Infrastructure.Administration;

public sealed class OpenApiPlatformAdministrationStore : IPlatformAdministrationStore
{
    public Task<IReadOnlyList<PlatformUserView>> ListUsersAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<PlatformUserView>>([]);
    public Task<PlatformUserView?> FindUserAsync(Guid userId, CancellationToken cancellationToken) =>
        Task.FromResult<PlatformUserView?>(null);
    public Task<CreateBotResult> CreateBotAsync(
        string userName, UserRole role, DateTimeOffset now, CancellationToken cancellationToken) =>
        Task.FromResult(new CreateBotResult(CreateBotState.UserNameConflict));
    public Task<UpdatePlatformRoleResult> UpdateRoleAsync(
        Guid userId, UserRole role, DateTimeOffset now, CancellationToken cancellationToken) =>
        Task.FromResult(new UpdatePlatformRoleResult(UpdatePlatformRoleState.UserNotFound));
    public Task<PlatformUserView?> InvalidateTokensAsync(
        Guid userId, DateTimeOffset now, CancellationToken cancellationToken) =>
        Task.FromResult<PlatformUserView?>(null);
    public Task<IReadOnlyList<DeadLetterView>> ListDeadLettersAsync(
        int limit, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<DeadLetterView>>([]);
    public Task<DeadLetterView?> FindDeadLetterAsync(
        Guid messageId, CancellationToken cancellationToken) =>
        Task.FromResult<DeadLetterView?>(null);
    public Task<bool> RequeueDeadLetterAsync(
        Guid messageId, CancellationToken cancellationToken) =>
        Task.FromResult(false);
}

public sealed class OpenApiUserAccountAdministrationStore : IUserAccountAdministrationStore
{
    public Task<UserDeletionPreview?> PreviewDeletionAsync(
        Guid userId,
        Guid actorUserId,
        CancellationToken cancellationToken) =>
        Task.FromResult<UserDeletionPreview?>(null);

    public Task<UserDeletionStoreResult> DeleteAsync(
        Guid userId,
        Guid actorUserId,
        UserDeletionMode mode,
        string reason,
        DateTimeOffset now,
        CancellationToken cancellationToken) =>
        Task.FromResult(new UserDeletionStoreResult(UserDeletionState.UserNotFound));
}

public sealed class OpenApiPlatformLogReader : IPlatformLogReader
{
    public Task<PlatformLogQueryResult> QueryAsync(
        PlatformLogQuery query,
        CancellationToken cancellationToken) =>
        Task.FromResult(new PlatformLogQueryResult(PlatformLogReadState.Available, []));

    public Task<PlatformLogExportResult> ExportAsync(
        PlatformLogQuery query,
        CancellationToken cancellationToken) =>
        Task.FromResult(new PlatformLogExportResult(
            PlatformLogReadState.Available,
            new PlatformLogExport(new MemoryStream(), "platform-logs.jsonl")));
}

public sealed class OpenApiPlatformAuditLogStore : IPlatformAuditLogStore
{
    public Task<IReadOnlyList<PlatformAuditView>> QueryAsync(
        PlatformAuditQuery query,
        CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<PlatformAuditView>>([]);
}
