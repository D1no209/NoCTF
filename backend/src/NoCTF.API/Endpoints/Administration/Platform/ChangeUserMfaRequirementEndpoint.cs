using FastEndpoints;
using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Options;
using System.Text.Json.Serialization;
using NoCTF.API.Security;
using NoCTF.API.Serialization;
using NoCTF.API.Endpoints.Authentication;
using NoCTF.API.Endpoints.Authentication.Mfa;
using NoCTF.Application.Authentication.Mfa;
using NoCTF.Domain.Identity.Mfa;

namespace NoCTF.API.Endpoints.Administration.Platform;

public sealed class ChangeUserMfaRequirementRequest { public bool Required { get; set; } }
public sealed class ChangeUserMfaRequirementValidator : Validator<ChangeUserMfaRequirementRequest> { }
public sealed class ChangeUserMfaRequirementEndpoint(IMfaManagementStore management, MfaBrowserFlow browser, IOptions<RefreshHttpOptions> options)
    : Endpoint<ChangeUserMfaRequirementRequest, Results<Ok<MfaChangeResult>, ProblemHttpResult>>
{
    public override void Configure() { Patch("/admin/platform/users/{userId}/mfa-requirement"); AuthSchemes("Bearer"); Roles("Administrator"); }
    public override async Task<Results<Ok<MfaChangeResult>, ProblemHttpResult>> ExecuteAsync(ChangeUserMfaRequirementRequest request, CancellationToken ct)
    {
        HttpContext.Response.Headers.CacheControl = "private, no-store";
        if (!RefreshRequestGuard.IsAllowed(HttpContext.Request, options.Value)) return MfaEndpointResults.Failure(MfaFailure.InvalidBrowser);
        var actor = MfaActorMapping.Read(User); var proof = browser.Read(HttpContext);
        if (actor is null || proof is null) return MfaEndpointResults.Failure(MfaFailure.StepUpRequired);
        var result = await management.ChangeRequirementAsync(actor, proof, Route<Guid>("userId"), request.Required, ct);
        return result.Succeeded ? TypedResults.Ok(result.Value!) : MfaEndpointResults.Failure(result.FailureCode!.Value);
    }
}
