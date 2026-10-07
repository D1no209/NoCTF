using FastEndpoints;
using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Options;
using NoCTF.API.Composition;
using NoCTF.API.Security;
using NoCTF.Application.Authentication.Mfa;

namespace NoCTF.API.Endpoints.Authentication.Mfa;

public sealed class ConfirmMfaEnrollmentRequest { public string Code { get; set; } = string.Empty; }
public sealed class ConfirmMfaEnrollmentValidator : Validator<ConfirmMfaEnrollmentRequest>
{
    public ConfirmMfaEnrollmentValidator() => RuleFor(value => value.Code).Matches("^[0-9]{6}$");
}
public sealed class ConfirmMfaEnrollmentEndpoint(IMfaAuthenticationStore mfa, MfaBrowserFlow browser, IOptions<RefreshHttpOptions> options, CompleteAuthentication complete) : Endpoint<ConfirmMfaEnrollmentRequest, Results<Ok<AuthenticationResponse>, ProblemHttpResult>>
{
    public override void Configure() { Post("/auth/mfa/enrollment/confirm"); Policies(AuthenticationRegistration.MfaFlowScheme); }
    public override async Task<Results<Ok<AuthenticationResponse>, ProblemHttpResult>> ExecuteAsync(ConfirmMfaEnrollmentRequest request, CancellationToken ct)
    {
        HttpContext.Response.Headers.CacheControl = "private, no-store";
        if (!RefreshRequestGuard.IsAllowed(HttpContext.Request, options.Value)) return MfaEndpointResults.Failure(MfaFailure.InvalidBrowser);
        var credential = browser.Read(HttpContext);
        if (credential is null) return MfaEndpointResults.Failure(MfaFailure.FlowExpired);
        var outcome = await mfa.ConfirmEnrollmentAsync(credential, request.Code, ct);
        if (!outcome.Succeeded) return MfaEndpointResults.Failure(outcome.FailureCode!.Value);
        return TypedResults.Ok(AuthenticationResponseMapping.Map(complete.Issue(outcome.Value!), HttpContext, options.Value, browser));
    }
}
