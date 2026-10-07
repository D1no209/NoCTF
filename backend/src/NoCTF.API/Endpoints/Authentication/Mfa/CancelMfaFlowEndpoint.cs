using FastEndpoints;
using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Options;
using NoCTF.API.Composition;
using NoCTF.API.Security;
using NoCTF.Application.Authentication.Mfa;

namespace NoCTF.API.Endpoints.Authentication.Mfa;

public sealed class CancelMfaFlowEndpoint(IMfaAuthenticationStore mfa, MfaBrowserFlow browser, IOptions<RefreshHttpOptions> options) : EndpointWithoutRequest<Results<NoContent, ProblemHttpResult>>
{
    public override void Configure() { Delete("/auth/mfa/flow"); Policies(AuthenticationRegistration.MfaFlowScheme); }
    public override async Task<Results<NoContent, ProblemHttpResult>> ExecuteAsync(CancellationToken ct)
    {
        HttpContext.Response.Headers.CacheControl = "private, no-store";
        if (!RefreshRequestGuard.IsAllowed(HttpContext.Request, options.Value)) return MfaEndpointResults.Failure(MfaFailure.InvalidBrowser);
        var credential = browser.Read(HttpContext);
        if (credential is null) return MfaEndpointResults.Failure(MfaFailure.FlowExpired);
        await mfa.CancelFlowAsync(credential, ct); browser.Clear(HttpContext);
        return TypedResults.NoContent();
    }
}
