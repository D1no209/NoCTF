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
    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var credential = browser.Read(Context);
        if (credential is null) return AuthenticateResult.NoResult();
        var flow = await mfa.ReadFlowAsync(credential, Context.RequestAborted);
        if (!flow.Succeeded) return AuthenticateResult.Fail("MFA flow is invalid.");
        return AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim("mfa_challenge", credential.ChallengeId.ToString("N"))], Scheme.Name)), Scheme.Name));
    }
}
