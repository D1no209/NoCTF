using NoCTF.Application.Authentication.Account;
using NoCTF.Application.Common;
using NoCTF.Domain.Identity;

namespace NoCTF.Application.Authentication.RefreshJwt;

public sealed record RefreshAccessTokenResult(
    Guid UserId,
    string UserName,
    UserRole Role,
    bool EmailVerified,
    string AccessToken,
    DateTimeOffset AccessTokenExpiresAt,
    string RefreshToken);

public enum RefreshAccessTokenFailureCode
{
    RefreshInvalid,
    UserNotFound
}

public sealed class RefreshAccessToken(
    IUserAuthenticationStore store,
    IAccessTokenIssuer issuer,
    TimeProvider timeProvider)
{
    public async Task<OperationResult<RefreshAccessTokenResult, RefreshAccessTokenFailureCode>> ExecuteAsync(
        string refreshToken,
        CancellationToken cancellationToken = default)
    {
        var principal = issuer.ValidateRefresh(refreshToken);
        if (principal is null)
            return OperationResult<RefreshAccessTokenResult, RefreshAccessTokenFailureCode>.Failure(
                RefreshAccessTokenFailureCode.RefreshInvalid, "Refresh token is invalid.");

        var user = await store.FindByIdAsync(principal.UserId, cancellationToken);
        if (user is null)
            return OperationResult<RefreshAccessTokenResult, RefreshAccessTokenFailureCode>.Failure(
                RefreshAccessTokenFailureCode.UserNotFound, "User no longer exists.");
        if (user.Kind != UserKind.Human)
            return OperationResult<RefreshAccessTokenResult, RefreshAccessTokenFailureCode>.Failure(
                RefreshAccessTokenFailureCode.RefreshInvalid,
                "Refresh token is no longer valid.");
        if (user.TokenVersion != principal.TokenVersion)
            return OperationResult<RefreshAccessTokenResult, RefreshAccessTokenFailureCode>.Failure(
                RefreshAccessTokenFailureCode.RefreshInvalid, "Refresh token is no longer valid.");
        var access = issuer.Issue(user, timeProvider.GetUtcNow());
        var replacement = issuer.IssueRefresh(user);
        return OperationResult<RefreshAccessTokenResult, RefreshAccessTokenFailureCode>.Success(
            new(
                user.Id,
                user.UserName,
                user.Role,
                user.EmailVerified,
                access.Token,
                access.ExpiresAt,
                replacement.Token));
    }
}
