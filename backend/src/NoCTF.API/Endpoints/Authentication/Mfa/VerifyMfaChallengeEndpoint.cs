using FastEndpoints;
using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Options;
using NoCTF.API.Composition;
using NoCTF.API.Security;
using NoCTF.Application.Authentication.Mfa;

namespace NoCTF.API.Endpoints.Authentication.Mfa;

public sealed class VerifyMfaChallengeRequest
{
    [System.Text.Json.Serialization.JsonConverter(typeof(NoCTF.API.Serialization.StrictPascalCaseEnumConverter<MfaVerificationMethod>))]
    public MfaVerificationMethod Method { get; set; }
    public string Code { get; set; } = string.Empty;
}
public sealed class VerifyMfaChallengeValidator : Validator<VerifyMfaChallengeRequest>
{
    public VerifyMfaChallengeValidator() { RuleFor(value => value.Method).IsInEnum(); RuleFor(value => value.Code).NotEmpty().MaximumLength(64); }
}
public sealed class VerifyMfaChallengeEndpoint(IMfaAuthenticationStore mfa, MfaBrowserFlow browser, IOptions<RefreshHttpOptions> options, CompleteAuthentication complete) : Endpoint<VerifyMfaChallengeRequest, Results<Ok<AuthenticationResponse>, ProblemHttpResult>>
{
    public override void Configure() { Post("/auth/mfa/verify"); Policies(AuthenticationRegistration.MfaFlowScheme); }
    public override async Task<Results<Ok<AuthenticationResponse>, ProblemHttpResult>> ExecuteAsync(VerifyMfaChallengeRequest request, CancellationToken ct)
    {
        HttpContext.Response.Headers.CacheControl = "private, no-store";
        if (!RefreshRequestGuard.IsAllowed(HttpContext.Request, options.Value)) return MfaEndpointResults.Failure(MfaFailure.InvalidBrowser);
        var credential = browser.Read(HttpContext);
        if (credential is null) return MfaEndpointResults.Failure(MfaFailure.FlowExpired);
        var outcome = await mfa.VerifyAsync(credential, new(request.Method, request.Code), ct);
        if (!outcome.Succeeded) return MfaEndpointResults.Failure(outcome.FailureCode!.Value);
        return TypedResults.Ok(AuthenticationResponseMapping.Map(complete.Issue(outcome.Value!), HttpContext, options.Value, browser));
    }
}
