using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using NoCTF.Application.Authentication.Mfa;

namespace NoCTF.API.Security;

public sealed class MfaFlowAuthenticationHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger,
    UrlEncoder encoder, MfaBrowserFlow browser, IMfaAuthenticationStore mfa)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    private MfaFailure failure = MfaFailure.FlowExpired;

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var credential = browser.Read(Context);
        if (credential is null) return AuthenticateResult.NoResult();
        NoCTF.Application.Common.OperationResult<MfaFlowView, MfaFailure> flow;
        try { flow = await mfa.ReadFlowAsync(credential, Context.RequestAborted); }
        catch (Exception exception) when (exception is System.Data.Common.DbException or Microsoft.EntityFrameworkCore.DbUpdateException or System.Security.Cryptography.CryptographicException)
        { failure = MfaFailure.DependencyUnavailable; return AuthenticateResult.Fail("MFA dependency is unavailable."); }
        if (!flow.Succeeded) { failure = flow.FailureCode!.Value; return AuthenticateResult.Fail("MFA flow is invalid."); }
        return AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim("mfa_challenge", credential.ChallengeId.ToString("N"))], Scheme.Name)), Scheme.Name));
    }
    protected override Task HandleChallengeAsync(AuthenticationProperties properties) =>
        NoCTF.API.Endpoints.Authentication.Mfa.MfaEndpointResults.Failure(failure).ExecuteAsync(Context);
}
