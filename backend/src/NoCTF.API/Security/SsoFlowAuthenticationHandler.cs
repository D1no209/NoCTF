using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace NoCTF.API.Security;

public sealed class SsoFlowAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    SsoBrowserCorrelation correlation)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string BrowserClaim = "noctf_sso_browser";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var browserId = correlation.Read(Context);
        if (browserId is null)
            return Task.FromResult(AuthenticateResult.NoResult());
        var identity = new ClaimsIdentity(
            [new Claim(BrowserClaim, browserId)],
            Scheme.Name);
        return Task.FromResult(AuthenticateResult.Success(
            new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name)));
    }
}
