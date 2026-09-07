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
    TimeProvider timeProvider,
    NoCTF.Application.Authentication.Privacy.IAccountActivityRecorder? activities = null,
    NoCTF.Application.Admission.ICredentialWorkAdmission? admission = null)
{
    public async Task<OperationResult<LoginResult, LoginFailureCode>> ExecuteAsync(
        LoginCommand command,
        CancellationToken cancellationToken = default)
    {
        var user = await store.FindByLoginAsync(command.Login.Trim(), cancellationToken);
        await using var lease = admission is null ? null : await admission.AcquireAsync(
            user?.Id.ToString("N") ?? command.Login.Trim().ToUpperInvariant(), cancellationToken);
        cancellationToken = lease?.Token ?? cancellationToken;
        if (user is null
            || user.Kind != UserKind.Human
            || !await store.VerifyPasswordAsync(user.Id, command.Password, cancellationToken))
        {
            if (activities is not null)
                await activities.RecordLoginAsync(null, timeProvider.GetUtcNow(), cancellationToken);
            return OperationResult<LoginResult, LoginFailureCode>.Failure(
                LoginFailureCode.InvalidCredentials,
                "Invalid credentials.");
        }

        var token = issuer.Issue(user, timeProvider.GetUtcNow());
        var refreshToken = issuer.IssueRefresh(user);
        if (activities is not null)
            await activities.RecordLoginAsync(user.Id, timeProvider.GetUtcNow(), cancellationToken);
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
