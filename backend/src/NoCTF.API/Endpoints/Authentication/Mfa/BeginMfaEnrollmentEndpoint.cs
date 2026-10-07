using FastEndpoints;
using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Options;
using NoCTF.API.Composition;
using NoCTF.API.Security;
using NoCTF.Application.Authentication.Mfa;

namespace NoCTF.API.Endpoints.Authentication.Mfa;

public sealed class BeginMfaEnrollmentEndpoint(IMfaAuthenticationStore mfa, IMfaManagementStore management, MfaBrowserFlow browser, IOptions<RefreshHttpOptions> options) : EndpointWithoutRequest<Results<Ok<MfaFlowResponse>, ProblemHttpResult>>
{
    public override void Configure() { Post("/auth/mfa/enrollment"); Policies("MfaEnrollment"); }
    public override async Task<Results<Ok<MfaFlowResponse>, ProblemHttpResult>> ExecuteAsync(CancellationToken ct)
    {
        HttpContext.Response.Headers.CacheControl = "private, no-store";
        if (!RefreshRequestGuard.IsAllowed(HttpContext.Request, options.Value)) return MfaEndpointResults.Failure(MfaFailure.InvalidBrowser);
        var credential = browser.Read(HttpContext);
        if (credential is null)
        {
            var actor = MfaActorMapping.Read(User);
            if (actor is null) return MfaEndpointResults.Failure(MfaFailure.PrimaryAuthenticationRequired);
            var started = await management.BeginOwnEnrollmentAsync(actor, ct);
            if (!started.Succeeded) return MfaEndpointResults.Failure(started.FailureCode!.Value);
            credential = started.Value!.Browser;
            browser.Write(HttpContext, credential, started.Value.Flow.ExpiresAt);
        }
        var outcome = await mfa.BeginEnrollmentAsync(credential, ct);
        return outcome.Succeeded ? TypedResults.Ok(MfaFlowResponse.From(outcome.Value!)) : MfaEndpointResults.Failure(outcome.FailureCode!.Value);
    }
}
