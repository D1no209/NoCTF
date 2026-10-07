using FastEndpoints;
using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Options;
using NoCTF.API.Endpoints.Authentication;
using NoCTF.API.Endpoints.Authentication.Mfa;
using NoCTF.API.Security;
using NoCTF.Application.Authentication.Mfa;

namespace NoCTF.API.Endpoints.Administration.Platform;

public sealed class ChangeOidcMfaTrustRequest
{
    public bool Enabled { get; set; }
    public int MaxAgeSeconds { get; set; } = 300;
    public string[] AcrValues { get; set; } = [];
    public string[][] AmrCombinations { get; set; } = [];
}
public sealed class ChangeOidcMfaTrustValidator : Validator<ChangeOidcMfaTrustRequest>
{
    public ChangeOidcMfaTrustValidator()
    {
        RuleFor(value => value.MaxAgeSeconds).InclusiveBetween(1, 300);
        RuleFor(value => value.AcrValues.Length).LessThanOrEqualTo(20);
        RuleFor(value => value.AmrCombinations.Length).LessThanOrEqualTo(20);
    }
}
public sealed class ChangeOidcMfaTrustEndpoint(IMfaManagementStore management, MfaBrowserFlow browser, IOptions<RefreshHttpOptions> options)
    : Endpoint<ChangeOidcMfaTrustRequest, Results<Ok<OidcMfaTrust>, ProblemHttpResult>>
{
    public override void Configure() { Put("/admin/platform/sso/providers/{providerId}/mfa-trust"); AuthSchemes("Bearer"); Roles("Administrator");
        Description(builder => builder.WithName("AdminPlatformMfaChangeOidcTrust"));
        Summary(summary => { summary.Summary = "Replaces an OIDC provider MFA trust policy and invalidates proofs issued under the previous policy."; summary.Description = "Replaces an OIDC provider MFA trust policy and invalidates proofs issued under the previous policy."; }); }
    public override async Task<Results<Ok<OidcMfaTrust>, ProblemHttpResult>> ExecuteAsync(ChangeOidcMfaTrustRequest request, CancellationToken ct)
    {
        if (!RefreshRequestGuard.IsAllowed(HttpContext.Request, options.Value)) return MfaEndpointResults.Failure(MfaFailure.InvalidBrowser);
        var actor = MfaActorMapping.Read(User); var proof = browser.Read(HttpContext);
        if (actor is null || proof is null) return MfaEndpointResults.Failure(MfaFailure.StepUpRequired);
        var result = await management.ChangeOidcTrustAsync(actor, proof, Route<Guid>("providerId"), request.Enabled, request.MaxAgeSeconds,
            request.AcrValues, request.AmrCombinations.Select(value => (IReadOnlyList<string>)value).ToArray(), ct);
        return result.Succeeded ? TypedResults.Ok(result.Value!) : MfaEndpointResults.Failure(result.FailureCode!.Value);
    }
}
