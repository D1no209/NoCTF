using NoCTF.Application.Authentication.Account;
using NoCTF.Application.Common;
using NoCTF.Domain.Identity;

namespace NoCTF.Application.Authentication.RefreshJwt;

public sealed record RefreshAccessTokenResult(
    Guid UserId,
    string UserName,
    UserRole Role,
    string AccessToken,
    DateTimeOffset AccessTokenExpiresAt,
    string RefreshToken);

public sealed class RefreshAccessToken(IUserAuthenticationStore store, IAccessTokenIssuer issuer)
{
    public async Task<OperationResult<RefreshAccessTokenResult>> ExecuteAsync(
        string refreshToken,
        CancellationToken cancellationToken = default)
    {
        var principal = issuer.ValidateRefresh(refreshToken);
        if (principal is null)
            return OperationResult<RefreshAccessTokenResult>.Failure("refresh_invalid", "Refresh token is invalid.");

        var user = await store.FindByIdAsync(principal.UserId, cancellationToken);
        if (user is null)
            return OperationResult<RefreshAccessTokenResult>.Failure("user_not_found", "User no longer exists.");
        if (user.Kind != UserKind.Human)
            return OperationResult<RefreshAccessTokenResult>.Failure(
                "refresh_invalid",
                "Refresh token is no longer valid.");
        if (user.TokenVersion != principal.TokenVersion)
            return OperationResult<RefreshAccessTokenResult>.Failure("refresh_invalid", "Refresh token is no longer valid.");
        var access = issuer.Issue(user, DateTimeOffset.UtcNow);
        var replacement = issuer.IssueRefresh(user);
        return OperationResult<RefreshAccessTokenResult>.Success(
            new(user.Id, user.UserName, user.Role, access.Token, access.ExpiresAt, replacement.Token));
    }
}
