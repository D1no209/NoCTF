using NoCTF.Application.Administration;
using NoCTF.Application.Administration.UserAccounts;
using NoCTF.Application.Administration.PlatformLogs;
using NoCTF.Domain.Identity;

namespace NoCTF.Infrastructure.Administration;

public sealed class NoOpPlatformAdministrationStore : IPlatformAdministrationStore
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
    public Task<UpdatePlatformUserStatusResult> UpdateAccountStatusAsync(
        Guid userId,
        Guid actorUserId,
        UserAccountStatus accountStatus,
        DateTimeOffset now,
        CancellationToken cancellationToken) =>
        Task.FromResult(new UpdatePlatformUserStatusResult(
            UpdatePlatformUserStatusState.UserNotFound));
    public Task<UpdatePlatformUserEmailVerificationResult> UpdateEmailVerificationAsync(
        Guid userId,
        Guid actorUserId,
        bool emailVerified,
        DateTimeOffset now,
        CancellationToken cancellationToken) =>
        Task.FromResult(new UpdatePlatformUserEmailVerificationResult(
            UpdatePlatformUserEmailVerificationState.UserNotFound));
    public Task<PatchPlatformUserResult> PatchUserAsync(
        Guid userId,
        Guid actorUserId,
        Action<User> apply,
        bool? emailVerified,
        DateTimeOffset now,
        CancellationToken cancellationToken) =>
        Task.FromResult(new PatchPlatformUserResult(PatchPlatformUserState.UserNotFound));
    public Task RecordTokenIssuedAsync(
        Guid actorUserId,
        PlatformUserTokenAuditFact fact,
        DateTimeOffset now,
        CancellationToken cancellationToken) => Task.CompletedTask;
    public Task<IReadOnlyList<AdminIssuedAccessTokenView>> ListIssuedTokensAsync(
        Guid actorUserId,
        Guid targetUserId,
        DateTimeOffset now,
        CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<AdminIssuedAccessTokenView>>([]);
    public Task<RevokeAdminIssuedAccessTokenState> RevokeIssuedTokenAsync(
        Guid actorUserId,
        Guid targetUserId,
        Guid jwtId,
        DateTimeOffset now,
        CancellationToken cancellationToken) =>
        Task.FromResult(RevokeAdminIssuedAccessTokenState.NotFound);
    public Task<PlatformUserView?> InvalidateTokensAsync(
        Guid userId,
        Guid actorUserId,
        DateTimeOffset now,
        CancellationToken cancellationToken) =>
        Task.FromResult<PlatformUserView?>(null);
}

public sealed class NoOpUserAccountAdministrationStore : IUserAccountAdministrationStore
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

public sealed class NoOpPlatformLogReader : IPlatformLogReader
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

public sealed class NoOpPlatformAuditLogStore : IPlatformAuditLogStore
{
    public Task<IReadOnlyList<PlatformAuditView>> QueryAsync(
        PlatformAuditQuery query,
        CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<PlatformAuditView>>([]);
}
