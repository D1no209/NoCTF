using NoCTF.Application.Authentication.Account;
using NoCTF.Application.Common;
using NoCTF.Domain.Identity;

namespace NoCTF.Application.Authentication.Login;

public sealed record LoginCommand(string Login, string Password);

public sealed record LoginResult(
    Guid UserId,
    string UserName,
    UserRole Role,
    bool EmailVerified,
    string AccessToken,
    DateTimeOffset AccessTokenExpiresAt,
    string RefreshToken);

public enum LoginFailureCode
{
    InvalidCredentials
}

public sealed class LoginUser(
    IUserAuthenticationStore store,
    IAccessTokenIssuer issuer,
    TimeProvider timeProvider)
{
    public async Task<OperationResult<LoginResult, LoginFailureCode>> ExecuteAsync(
        LoginCommand command,
        CancellationToken cancellationToken = default)
    {
        var user = await store.FindByLoginAsync(command.Login.Trim(), cancellationToken);
        if (user is null
            || user.Kind != UserKind.Human
            || !await store.VerifyPasswordAsync(user.Id, command.Password, cancellationToken))
            return OperationResult<LoginResult, LoginFailureCode>.Failure(
                LoginFailureCode.InvalidCredentials,
                "Invalid credentials.");

        var token = issuer.Issue(user, timeProvider.GetUtcNow());
        var refreshToken = issuer.IssueRefresh(user);
        return OperationResult<LoginResult, LoginFailureCode>.Success(new(
            user.Id,
            user.UserName,
            user.Role,
            user.EmailVerified,
            token.Token,
            token.ExpiresAt,
            refreshToken.Token));
    }
}
