using NoCTF.Application.Authentication.Account;
using NoCTF.Application.Common;
using NoCTF.Domain.Identity;
using NoCTF.Domain.Identity.Mfa;
using NoCTF.Application.Authentication.Mfa;

namespace NoCTF.Application.Authentication.Login;

public sealed record LoginCommand(string Login, string Password, MfaBrowserCredential? Recovery = null);

public enum LoginFailureCode
{
    InvalidCredentials,
    DependencyUnavailable,
    InvalidRecoveryGrant
}

public sealed class LoginUser(
    IUserAuthenticationStore store,
    CompleteAuthentication complete,
    TimeProvider timeProvider,
    NoCTF.Application.Authentication.Privacy.IAccountActivityRecorder? activities = null,
    NoCTF.Application.Admission.ICredentialWorkAdmission? admission = null)
{
    public async Task<OperationResult<AuthenticationCompletion, LoginFailureCode>> ExecuteAsync(
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
            return OperationResult<AuthenticationCompletion, LoginFailureCode>.Failure(
                LoginFailureCode.InvalidCredentials,
                "Invalid credentials.");
        }

        var completion = await complete.ExecuteAsync(new(user, AuthenticationMethod.Password, timeProvider.GetUtcNow()), cancellationToken, command.Recovery);
        if (!completion.Succeeded)
            return OperationResult<AuthenticationCompletion, LoginFailureCode>.Failure(
                completion.FailureCode switch { MfaFailure.AccountUnavailable => LoginFailureCode.InvalidCredentials, MfaFailure.InvalidRecoveryGrant => LoginFailureCode.InvalidRecoveryGrant, _ => LoginFailureCode.DependencyUnavailable },
                "Authentication could not be completed.");
        if (completion.Value!.State == AuthenticationState.Authenticated && activities is not null)
            await activities.RecordLoginAsync(user.Id, timeProvider.GetUtcNow(), cancellationToken);
        return OperationResult<AuthenticationCompletion, LoginFailureCode>.Success(completion.Value!);
    }
}
