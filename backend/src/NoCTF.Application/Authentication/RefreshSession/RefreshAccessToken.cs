using NoCTF.Application.Authentication.Ports;
using NoCTF.Application.Common;

namespace NoCTF.Application.Authentication.RefreshSession;

public sealed record RefreshAccessTokenResult(
    Guid UserId,
    string UserName,
    string Role,
    string AccessToken,
    DateTimeOffset AccessTokenExpiresAt,
    string RefreshToken);

public sealed class RefreshAccessToken(IUserAuthenticationStore store, IAccessTokenIssuer issuer)
{
    public async Task<OperationResult<RefreshAccessTokenResult>> ExecuteAsync(
        string refreshTokenHash,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        var rotation = await store.RotateRefreshAsync(refreshTokenHash, now, cancellationToken);
        if (rotation is null)
            return OperationResult<RefreshAccessTokenResult>.Failure("refresh_invalid", "Refresh session is invalid.");
        if (rotation.ReplayDetected)
        {
            await store.RevokeRefreshFamilyAsync(rotation.FamilyId, now, cancellationToken);
            return OperationResult<RefreshAccessTokenResult>.Failure("refresh_replay", "Refresh token replay detected.");
        }

        var user = await store.FindByIdAsync(rotation.UserId, cancellationToken);
        if (user is null)
            return OperationResult<RefreshAccessTokenResult>.Failure("user_not_found", "User no longer exists.");
        var access = issuer.Issue(user);
        return OperationResult<RefreshAccessTokenResult>.Success(
            new(user.Id, user.UserName, user.Role, access.Token, access.ExpiresAt, rotation.RefreshToken));
    }
}
