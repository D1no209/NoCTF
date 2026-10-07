using FastEndpoints;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Endpoints.Authentication.Mfa;
using NoCTF.API.Serialization;
using NoCTF.Application.Authentication.Mfa;
using NoCTF.Domain.Identity.Mfa;
using System.Text.Json.Serialization;

namespace NoCTF.API.Endpoints.Administration.Platform;

public sealed record OidcMfaTrustResponse(Guid ProviderId, string Name, bool Enabled, int MaxAgeSeconds,
    IReadOnlyList<string> AcrValues, IReadOnlyList<IReadOnlyList<string>> AmrCombinations);
public sealed record PlatformMfaConfigurationResponse(
    [property: JsonConverter(typeof(StrictPascalCaseEnumConverter<MfaPolicy>))] MfaPolicy Policy,
    IReadOnlyList<OidcMfaTrustResponse> Providers);

public sealed class GetPlatformMfaConfigurationEndpoint(IMfaManagementStore store)
    : EndpointWithoutRequest<Results<Ok<PlatformMfaConfigurationResponse>, ProblemHttpResult>>
{
    public override void Configure() { Get("/admin/platform/mfa"); AuthSchemes("Bearer"); Roles("Administrator"); }
    public override async Task<Results<Ok<PlatformMfaConfigurationResponse>, ProblemHttpResult>> ExecuteAsync(CancellationToken ct)
    {
        HttpContext.Response.Headers.CacheControl = "private, no-store";
        var actor = MfaActorMapping.Read(User);
        if (actor is null) return MfaEndpointResults.Failure(MfaFailure.NotApplicable);
        var result = await store.ReadConfigurationAsync(actor, ct);
        if (!result.Succeeded) return MfaEndpointResults.Failure(result.FailureCode!.Value);
        return TypedResults.Ok(new PlatformMfaConfigurationResponse(result.Value!.Policy,
            result.Value.Providers.Select(value => new OidcMfaTrustResponse(value.ProviderId, value.Name, value.Trust.Enabled,
                value.Trust.MaxAgeSeconds, value.Trust.AcrValues, value.Trust.AmrCombinations)).ToArray()));
    }
}
