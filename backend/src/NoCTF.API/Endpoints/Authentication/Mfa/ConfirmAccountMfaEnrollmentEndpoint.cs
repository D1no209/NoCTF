using FastEndpoints;
using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Options;
using NoCTF.API.Composition;
using NoCTF.API.Security;
using NoCTF.Application.Authentication.Mfa;

namespace NoCTF.API.Endpoints.Authentication.Mfa;

public sealed class ConfirmAccountMfaEnrollmentRequest { public string Code { get; set; } = string.Empty; }
public sealed record ConfirmAccountMfaEnrollmentResponse(IReadOnlyList<string> RecoveryCodes);
public sealed class ConfirmAccountMfaEnrollmentValidator : Validator<ConfirmAccountMfaEnrollmentRequest>
{
    public ConfirmAccountMfaEnrollmentValidator() => RuleFor(value => value.Code).Matches("^[0-9]{6}$");
}

public sealed class ConfirmAccountMfaEnrollmentEndpoint(IMfaAuthenticationStore store, MfaBrowserFlow browser,
    IOptions<RefreshHttpOptions> options) : Endpoint<ConfirmAccountMfaEnrollmentRequest,
        Results<Ok<ConfirmAccountMfaEnrollmentResponse>, ProblemHttpResult>>
{
    public override void Configure() { Post("/auth/mfa/account/enrollment/confirm"); Policies(AuthenticationRegistration.MfaFlowScheme); }

    public override async Task<Results<Ok<ConfirmAccountMfaEnrollmentResponse>, ProblemHttpResult>> ExecuteAsync(
        ConfirmAccountMfaEnrollmentRequest request, CancellationToken ct)
    {
        HttpContext.Response.Headers.CacheControl = "private, no-store";
        if (!RefreshRequestGuard.IsAllowed(HttpContext.Request, options.Value)) return MfaEndpointResults.Failure(MfaFailure.InvalidBrowser);
        var credential = browser.Read(HttpContext);
        if (credential is null) return MfaEndpointResults.Failure(MfaFailure.FlowExpired);
        var result = await store.ConfirmEnrollmentAsync(credential, request.Code, ct, accountManagement: true);
        if (!result.Succeeded) return MfaEndpointResults.Failure(result.FailureCode!.Value);
        browser.Clear(HttpContext);
        HttpContext.Response.Cookies.Delete(RefreshCookie.Name(options.Value), RefreshCookie.DeleteOptions(options.Value));
        return TypedResults.Ok(new ConfirmAccountMfaEnrollmentResponse(result.Value!.RecoveryCodes!));
    }
}
