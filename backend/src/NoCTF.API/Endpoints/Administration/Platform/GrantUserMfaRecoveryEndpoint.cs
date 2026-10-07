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

public sealed class GrantUserMfaRecoveryRequest { public string Reason { get; set; } = string.Empty; }
public sealed class GrantUserMfaRecoveryValidator : Validator<GrantUserMfaRecoveryRequest>
{
    public GrantUserMfaRecoveryValidator() => RuleFor(value => value.Reason).NotEmpty().MaximumLength(1024);
}
public sealed class GrantUserMfaRecoveryEndpoint(IMfaManagementStore management, MfaBrowserFlow browser, IOptions<RefreshHttpOptions> options)
    : Endpoint<GrantUserMfaRecoveryRequest, Results<Ok<MfaRecoveryGrant>, ProblemHttpResult>>
{
    public override void Configure() { Post("/admin/platform/users/{userId}/mfa-recovery"); AuthSchemes("Bearer"); Roles("Administrator"); }
    public override async Task<Results<Ok<MfaRecoveryGrant>, ProblemHttpResult>> ExecuteAsync(GrantUserMfaRecoveryRequest request, CancellationToken ct)
    {
        HttpContext.Response.Headers.CacheControl = "private, no-store";
        if (!RefreshRequestGuard.IsAllowed(HttpContext.Request, options.Value)) return MfaEndpointResults.Failure(MfaFailure.InvalidBrowser);
        var actor = MfaActorMapping.Read(User); var proof = browser.Read(HttpContext);
        if (actor is null || proof is null) return MfaEndpointResults.Failure(MfaFailure.StepUpRequired);
        var result = await management.GrantRecoveryAsync(actor, proof, Route<Guid>("userId"), request.Reason, ct);
        return result.Succeeded ? TypedResults.Ok(result.Value!) : MfaEndpointResults.Failure(result.FailureCode!.Value);
    }
}
