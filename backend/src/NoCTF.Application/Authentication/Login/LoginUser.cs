using NoCTF.Application.Authentication.Ports;
using NoCTF.Application.Common;

namespace NoCTF.Application.Authentication.Login;

public sealed record LoginCommand(string Login, string Password);

public sealed record LoginResult(
    Guid UserId,
    string UserName,
    string Role,
    string AccessToken,
    DateTimeOffset AccessTokenExpiresAt,
    string RefreshToken);

public sealed class LoginUser(IUserAuthenticationStore store, IAccessTokenIssuer issuer)
{
    public async Task<OperationResult<LoginResult>> ExecuteAsync(
        LoginCommand command,
        CancellationToken cancellationToken = default)
    {
        var user = await store.FindByLoginAsync(command.Login.Trim(), cancellationToken);
        if (user is null || !await store.VerifyPasswordAsync(user.Id, command.Password, cancellationToken))
            return OperationResult<LoginResult>.Failure("invalid_credentials", "Invalid credentials.");

        var token = issuer.Issue(user);
        var refreshToken = await store.CreateRefreshTokenAsync(
            user.Id,
            null,
            DateTimeOffset.UtcNow,
            cancellationToken);
        return OperationResult<LoginResult>.Success(new(
            user.Id, user.UserName, user.Role, token.Token, token.ExpiresAt, refreshToken));
    }
}
