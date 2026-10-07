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

public sealed class ChangePlatformMfaPolicyRequest
{
    [JsonConverter(typeof(StrictPascalCaseEnumConverter<MfaPolicy>))] public MfaPolicy Policy { get; set; }
}
public sealed class ChangePlatformMfaPolicyValidator : Validator<ChangePlatformMfaPolicyRequest>
{
    public ChangePlatformMfaPolicyValidator() => RuleFor(value => value.Policy).IsInEnum();
}
public sealed record PlatformMfaPolicyResponse([property: JsonConverter(typeof(StrictPascalCaseEnumConverter<MfaPolicy>))] MfaPolicy Policy);
public sealed class ChangePlatformMfaPolicyEndpoint(IMfaManagementStore management, MfaBrowserFlow browser, IOptions<RefreshHttpOptions> options)
    : Endpoint<ChangePlatformMfaPolicyRequest, Results<Ok<PlatformMfaPolicyResponse>, ProblemHttpResult>>
{
    public override void Configure() { Patch("/admin/platform/mfa-policy"); AuthSchemes("Bearer"); Roles("Administrator");
        Description(builder => builder.WithName("AdminPlatformMfaChangePolicy"));
        Summary(summary => { summary.Summary = "Changes the platform MFA policy after operation-bound local second-factor verification."; summary.Description = "Changes the platform MFA policy after operation-bound local second-factor verification."; }); }
    public override async Task<Results<Ok<PlatformMfaPolicyResponse>, ProblemHttpResult>> ExecuteAsync(ChangePlatformMfaPolicyRequest request, CancellationToken ct)
    {
        HttpContext.Response.Headers.CacheControl = "private, no-store";
        if (!RefreshRequestGuard.IsAllowed(HttpContext.Request, options.Value)) return MfaEndpointResults.Failure(MfaFailure.InvalidBrowser);
        var actor = MfaActorMapping.Read(User); var proof = browser.Read(HttpContext);
        if (actor is null || proof is null) return MfaEndpointResults.Failure(MfaFailure.StepUpRequired);
        var result = await management.ChangePolicyAsync(actor, proof, request.Policy, ct);
        return result.Succeeded ? TypedResults.Ok(new PlatformMfaPolicyResponse(result.Value)) : MfaEndpointResults.Failure(result.FailureCode!.Value);
    }
}
