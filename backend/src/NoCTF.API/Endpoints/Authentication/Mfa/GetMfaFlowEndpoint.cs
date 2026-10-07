using FastEndpoints;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Options;
using System.Text.Json.Serialization;
using NoCTF.API.Composition;
using NoCTF.API.Security;
using NoCTF.API.Serialization;
using NoCTF.Application.Authentication.Mfa;
using NoCTF.Domain.Identity.Mfa;

namespace NoCTF.API.Endpoints.Authentication.Mfa;

public sealed record MfaFlowResponse(Guid Id,
    [property: JsonConverter(typeof(StrictPascalCaseEnumConverter<MfaChallengePurpose>))] MfaChallengePurpose Purpose,
    DateTimeOffset ExpiresAt, int RemainingAttempts, string UserName, bool RecoveryAvailable, string ReturnPath,
    string? Secret, string? ProvisioningUri)
{
    public static MfaFlowResponse From(MfaFlowView value) => new(value.Id, value.Purpose, value.ExpiresAt, value.RemainingAttempts,
        value.UserName, value.RecoveryAvailable, value.ReturnPath, value.Secret, value.ProvisioningUri);
}

public sealed record MfaFailureResponse(
    [property: JsonConverter(typeof(StrictPascalCaseEnumConverter<MfaFailure>))] MfaFailure Code,
    string Message);

public static class MfaEndpointResults
{
    public static ProblemHttpResult Failure(MfaFailure failure) => TypedResults.Problem(statusCode: failure switch
    {
        MfaFailure.FlowExpired => 410, MfaFailure.RateLimited or MfaFailure.AttemptsExceeded => 429,
        MfaFailure.DependencyUnavailable => 503, MfaFailure.InvalidCode or MfaFailure.CodeAlreadyUsed => 400, _ => 403
    }, title: "MFA verification could not be completed.", extensions: new Dictionary<string, object?> { ["code"] = failure.ToString() });
}

public sealed class GetMfaFlowEndpoint(IMfaAuthenticationStore mfa, MfaBrowserFlow browser)
    : EndpointWithoutRequest<Results<Ok<MfaFlowResponse>, ProblemHttpResult>>
{
    public override void Configure() { Get("/auth/mfa/flow"); Policies(AuthenticationRegistration.MfaFlowScheme); }
    public override async Task<Results<Ok<MfaFlowResponse>, ProblemHttpResult>> ExecuteAsync(CancellationToken ct)
    {
        HttpContext.Response.Headers.CacheControl = "private, no-store";
        var credential = browser.Read(HttpContext);
        if (credential is null) return MfaEndpointResults.Failure(MfaFailure.FlowExpired);
        var result = await mfa.ReadFlowAsync(credential, ct);
        return result.Succeeded ? TypedResults.Ok(MfaFlowResponse.From(result.Value!)) : MfaEndpointResults.Failure(result.FailureCode!.Value);
    }
}
