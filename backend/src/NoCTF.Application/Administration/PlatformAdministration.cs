using NoCTF.Domain.Identity;

namespace NoCTF.Application.Administration;

public sealed record PlatformUserView(
    Guid Id,
    string UserName,
    string Email,
    UserKind Kind,
    UserRole Role,
    int TokenVersion,
    bool EmailVerified,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record DeadLetterView(
    Guid MessageId,
    string MessageType,
    string Source,
    string ExceptionType,
    DateTimeOffset SentAt,
    bool Replayable);

public interface IPlatformAdministrationStore
{
    Task<IReadOnlyList<PlatformUserView>> ListUsersAsync(CancellationToken cancellationToken);
    Task<PlatformUserView?> FindUserAsync(Guid userId, CancellationToken cancellationToken);
    Task<PlatformUserView?> UpdateRoleAsync(
        Guid userId,
        UserRole role,
        DateTimeOffset now,
        CancellationToken cancellationToken);
    Task<PlatformUserView?> InvalidateTokensAsync(
        Guid userId,
        DateTimeOffset now,
        CancellationToken cancellationToken);
    Task<IReadOnlyList<DeadLetterView>> ListDeadLettersAsync(
        int limit,
        CancellationToken cancellationToken);
    Task<DeadLetterView?> FindDeadLetterAsync(
        Guid messageId,
        CancellationToken cancellationToken);
    Task<bool> RequeueDeadLetterAsync(
        Guid messageId,
        CancellationToken cancellationToken);
}

public sealed class ManagePlatform(IPlatformAdministrationStore store)
{
    public Task<IReadOnlyList<PlatformUserView>> ListUsersAsync(CancellationToken ct = default) =>
        store.ListUsersAsync(ct);
    public Task<PlatformUserView?> GetUserAsync(Guid userId, CancellationToken ct = default) =>
        store.FindUserAsync(userId, ct);
    public Task<PlatformUserView?> UpdateRoleAsync(
        Guid userId,
        UserRole role,
        DateTimeOffset now,
        CancellationToken ct = default) =>
        store.UpdateRoleAsync(userId, role, now, ct);
    public Task<PlatformUserView?> InvalidateTokensAsync(
        Guid userId,
        DateTimeOffset now,
        CancellationToken ct = default) =>
        store.InvalidateTokensAsync(userId, now, ct);
    public Task<IReadOnlyList<DeadLetterView>> ListDeadLettersAsync(
        int limit,
        CancellationToken ct = default) =>
        store.ListDeadLettersAsync(limit, ct);
    public Task<DeadLetterView?> GetDeadLetterAsync(
        Guid messageId,
        CancellationToken ct = default) =>
        store.FindDeadLetterAsync(messageId, ct);
    public Task<bool> RequeueDeadLetterAsync(
        Guid messageId,
        CancellationToken ct = default) =>
        store.RequeueDeadLetterAsync(messageId, ct);
}
