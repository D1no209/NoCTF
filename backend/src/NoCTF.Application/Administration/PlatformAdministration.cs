using NoCTF.Application.Authentication.Account;
using NoCTF.Domain.Identity;

namespace NoCTF.Application.Administration;

public sealed record PlatformUserView(
    Guid Id,
    string UserName,
    string Email,
    UserKind Kind,
    UserRole Role,
    UserAccountStatus AccountStatus,
    int TokenVersion,
    bool EmailVerified,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    Guid? SsoProviderId = null,
    SsoProtocol? SsoProtocol = null,
    string? SsoSubject = null,
    DateTimeOffset? SsoBoundAt = null);

public sealed record PlatformUserListQuery(
    string? Keyword,
    UserKind? Kind,
    UserRole? Role,
    int Offset,
    int Limit,
    bool Desc,
    Guid? SsoProviderId = null);

public sealed record PlatformUserListPage(
    IReadOnlyList<PlatformUserView> Items,
    int Total);

public enum UpdatePlatformRoleState
{
    Updated,
    UserNotFound,
    ActiveOwnerOrManagerAssignments,
    LastAdministratorProtected
}

public sealed record PlatformRoleAssignmentBlockers(
    IReadOnlyList<Guid> CompetitionIds,
    IReadOnlyList<Guid> ChallengeIds);

public sealed record UpdatePlatformRoleResult(
    UpdatePlatformRoleState State,
    PlatformUserView? User = null,
    PlatformRoleAssignmentBlockers? Blockers = null);

public enum UpdatePlatformUserStatusState
{
    Updated,
    UserNotFound,
    AnonymizedAccountImmutable,
    LastAdministratorProtected
}

public sealed record UpdatePlatformUserStatusResult(
    UpdatePlatformUserStatusState State,
    PlatformUserView? User = null);

public enum UpdatePlatformUserEmailVerificationState
{
    Updated,
    UserNotFound,
    AnonymizedAccountImmutable
}

public sealed record UpdatePlatformUserEmailVerificationResult(
    UpdatePlatformUserEmailVerificationState State,
    PlatformUserView? User = null);

public enum PatchPlatformUserState
{
    Updated,
    UserNotFound,
    ActiveOwnerOrManagerAssignments,
    LastAdministratorProtected,
    AnonymizedAccountImmutable
}

public sealed record PatchPlatformUserResult(
    PatchPlatformUserState State,
    PlatformUserView? User = null,
    PlatformRoleAssignmentBlockers? Blockers = null);

public enum CreateBotState
{
    Created,
    UserNameConflict,
    InvalidUserName,
    InvalidRole
}

public sealed record CreateBotResult(
    CreateBotState State,
    PlatformUserView? User = null);

public enum IssuePlatformUserTokenFailure
{
    None,
    UserNotFound,
    AccountInactive,
    InvalidLifetime
}

public sealed record IssuePlatformUserTokenResult(
    IssuedAccessToken? Token,
    PlatformUserView? TargetUser,
    IssuePlatformUserTokenFailure Failure);

public enum PlatformUserTokenAdministrationAction
{
    AccessTokenIssued,
    AccessTokenRevoked,
    TokensInvalidated
}

public sealed record PlatformUserTokenAuditFact(
    int SchemaVersion,
    Guid TargetUserId,
    string TargetUserName,
    PlatformUserTokenAdministrationAction Action,
    Guid? JwtId,
    DateTimeOffset? ExpiresAt,
    string? Reason,
    int TokenVersion);

public static class BotIdentity
{
    public static string DummyEmail(Guid userId) => $"bot-{userId:N}@bot.invalid";
}

public interface IPlatformAdministrationStore
{
    Task<IReadOnlyList<PlatformUserView>> ListUsersAsync(CancellationToken cancellationToken);
    Task<PlatformUserListPage> ListUsersPageAsync(
        PlatformUserListQuery query,
        CancellationToken cancellationToken);
    Task<PlatformUserView?> FindUserAsync(Guid userId, CancellationToken cancellationToken);
    Task<CreateBotResult> CreateBotAsync(
        string userName,
        UserRole role,
        DateTimeOffset now,
        CancellationToken cancellationToken);
    Task<UpdatePlatformRoleResult> UpdateRoleAsync(
        Guid userId,
        UserRole role,
        DateTimeOffset now,
        CancellationToken cancellationToken);
    Task<UpdatePlatformUserStatusResult> UpdateAccountStatusAsync(
        Guid userId,
        Guid actorUserId,
        UserAccountStatus accountStatus,
        DateTimeOffset now,
        CancellationToken cancellationToken);
    Task<UpdatePlatformUserEmailVerificationResult> UpdateEmailVerificationAsync(
        Guid userId,
        Guid actorUserId,
        bool emailVerified,
        DateTimeOffset now,
        CancellationToken cancellationToken);
    Task<PatchPlatformUserResult> PatchUserAsync(
        Guid userId,
        Guid actorUserId,
        Action<User> apply,
        bool? emailVerified,
        DateTimeOffset now,
        CancellationToken cancellationToken);
    Task<PlatformUserView?> InvalidateTokensAsync(
        Guid userId,
        Guid actorUserId,
        DateTimeOffset now,
        CancellationToken cancellationToken);
}

public sealed class ManagePlatform(
    IPlatformAdministrationStore store,
    IAccessTokenIssuer tokenIssuer)
{
    public const long MinimumIssuedTokenLifetimeSeconds = 60;
    public const long MaximumIssuedTokenLifetimeSeconds = 31_536_000;

    public Task<IReadOnlyList<PlatformUserView>> ListUsersAsync(CancellationToken ct = default) =>
        store.ListUsersAsync(ct);
    public Task<PlatformUserListPage> ListUsersPageAsync(
        PlatformUserListQuery query,
        CancellationToken ct = default) =>
        store.ListUsersPageAsync(query, ct);
    public Task<PlatformUserView?> GetUserAsync(Guid userId, CancellationToken ct = default) =>
        store.FindUserAsync(userId, ct);
    public Task<CreateBotResult> CreateBotAsync(
        string userName,
        UserRole role,
        DateTimeOffset now,
        CancellationToken ct = default)
    {
        var normalized = userName.Trim();
        if (normalized.Length is < 3 or > 64
            || !normalized.All(character =>
                char.IsAsciiLetterOrDigit(character) || character is '_' or '-'))
            return Task.FromResult(new CreateBotResult(CreateBotState.InvalidUserName));
        if (!Enum.IsDefined(role))
            return Task.FromResult(new CreateBotResult(CreateBotState.InvalidRole));
        return store.CreateBotAsync(normalized, role, now, ct);
    }
    public async Task<IssuePlatformUserTokenResult> IssueUserTokenAsync(
        Guid userId,
        long expiresInSeconds,
        DateTimeOffset now,
        CancellationToken ct = default)
    {
        if (expiresInSeconds is < MinimumIssuedTokenLifetimeSeconds
            or > MaximumIssuedTokenLifetimeSeconds)
            return new(null, null, IssuePlatformUserTokenFailure.InvalidLifetime);
        var user = await store.FindUserAsync(userId, ct);
        if (user is null)
            return new(null, null, IssuePlatformUserTokenFailure.UserNotFound);
        if (user.AccountStatus != UserAccountStatus.Active)
            return new(null, user, IssuePlatformUserTokenFailure.AccountInactive);
        try
        {
            var lifetime = TimeSpan.FromTicks(checked(expiresInSeconds * TimeSpan.TicksPerSecond));
            var token = tokenIssuer.Issue(
                new AuthenticatedUser(
                    user.Id,
                    user.UserName,
                    user.Role,
                    user.Kind,
                    user.TokenVersion,
                    user.EmailVerified),
                now,
                lifetime);
            return new(token, user, IssuePlatformUserTokenFailure.None);
        }
        catch (ArgumentOutOfRangeException)
        {
            return new(null, user, IssuePlatformUserTokenFailure.InvalidLifetime);
        }
        catch (OverflowException)
        {
            return new(null, user, IssuePlatformUserTokenFailure.InvalidLifetime);
        }
    }
    public Task<UpdatePlatformRoleResult> UpdateRoleAsync(
        Guid userId,
        UserRole role,
        DateTimeOffset now,
        CancellationToken ct = default) =>
        store.UpdateRoleAsync(userId, role, now, ct);
    public Task<UpdatePlatformUserStatusResult> UpdateAccountStatusAsync(
        Guid userId,
        Guid actorUserId,
        UserAccountStatus accountStatus,
        DateTimeOffset now,
        CancellationToken ct = default) =>
        accountStatus == UserAccountStatus.Anonymized
            ? Task.FromResult(new UpdatePlatformUserStatusResult(
                UpdatePlatformUserStatusState.AnonymizedAccountImmutable))
            : store.UpdateAccountStatusAsync(
                userId,
                actorUserId,
                accountStatus,
                now,
                ct);
    public Task<UpdatePlatformUserEmailVerificationResult> UpdateEmailVerificationAsync(
        Guid userId,
        Guid actorUserId,
        bool emailVerified,
        DateTimeOffset now,
        CancellationToken ct = default) =>
        store.UpdateEmailVerificationAsync(userId, actorUserId, emailVerified, now, ct);
    public Task<PatchPlatformUserResult> PatchUserAsync(
        Guid userId,
        Guid actorUserId,
        Action<User> apply,
        bool? emailVerified,
        DateTimeOffset now,
        CancellationToken ct = default) =>
        store.PatchUserAsync(userId, actorUserId, apply, emailVerified, now, ct);
    public Task<PlatformUserView?> InvalidateTokensAsync(
        Guid userId,
        Guid actorUserId,
        DateTimeOffset now,
        CancellationToken ct = default) =>
        store.InvalidateTokensAsync(userId, actorUserId, now, ct);
}
