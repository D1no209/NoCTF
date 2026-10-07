using FastEndpoints;
using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Options;
using System.Text.Json.Serialization;
using NoCTF.API.Security;
using NoCTF.API.Serialization;
using NoCTF.Application.Authentication.Mfa;
using NoCTF.Domain.Identity.Mfa;

namespace NoCTF.API.Endpoints.Authentication.Mfa;

public sealed class BeginMfaRebindEndpoint(IMfaManagementStore management, MfaBrowserFlow browser, IOptions<RefreshHttpOptions> options)
    : EndpointWithoutRequest<Results<Ok<MfaFlowResponse>, ProblemHttpResult>>
{
    public override void Configure() { Post("/auth/mfa/rebind"); AuthSchemes("Bearer"); }
    public override async Task<Results<Ok<MfaFlowResponse>, ProblemHttpResult>> ExecuteAsync(CancellationToken ct)
    {
        HttpContext.Response.Headers.CacheControl = "private, no-store";
        if (!RefreshRequestGuard.IsAllowed(HttpContext.Request, options.Value)) return MfaEndpointResults.Failure(MfaFailure.InvalidBrowser);
        var actor = MfaActorMapping.Read(User); var proof = browser.Read(HttpContext);
        if (actor is null || proof is null) return MfaEndpointResults.Failure(MfaFailure.StepUpRequired);
        var result = await management.BeginRebindAsync(actor, proof, ct);
        if (!result.Succeeded) return MfaEndpointResults.Failure(result.FailureCode!.Value);
        browser.Write(HttpContext, result.Value!.Browser, result.Value.Flow.ExpiresAt);
        return TypedResults.Ok(MfaFlowResponse.From(result.Value.Flow));
    }
}
