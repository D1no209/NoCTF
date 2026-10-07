using FastEndpoints;
using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Options;
using NoCTF.API.Endpoints.Authentication.Mfa;
using NoCTF.Application.Authentication.Passkeys;

namespace NoCTF.API.Endpoints.Authentication.Passkeys;

public sealed class CompletePasskeyRegistrationRequest { public string CredentialJson { get; set; } = string.Empty; }
public sealed class CompletePasskeyRegistrationValidator : Validator<CompletePasskeyRegistrationRequest>
{
    public CompletePasskeyRegistrationValidator() => RuleFor(value => value.CredentialJson).NotEmpty().MaximumLength(65536);
}
public sealed class CompletePasskeyRegistrationEndpoint(IPasskeyStore store, PasskeyBrowserFlow browser,
    IOptions<RefreshHttpOptions> options) : Endpoint<CompletePasskeyRegistrationRequest, Results<Ok<PasskeyAccountCredential>, ProblemHttpResult>>
{
    public override void Configure() { Post("/auth/me/passkeys"); AuthSchemes("Bearer"); MaxRequestBodySize(70 * 1024); }
    public override async Task<Results<Ok<PasskeyAccountCredential>, ProblemHttpResult>> ExecuteAsync(CompletePasskeyRegistrationRequest request, CancellationToken ct)
    {
        HttpContext.Response.Headers.CacheControl = "private, no-store";
        var actor = MfaActorMapping.Read(User); var origin = PasskeyBrowserFlow.Origin(HttpContext.Request); var flow = browser.Read(HttpContext);
        if (actor is null) return PasskeyEndpointResults.Failure(PasskeyFailure.AccountUnavailable);
        if (origin is null) return PasskeyEndpointResults.Failure(PasskeyFailure.InvalidOrigin);
        if (flow is null) return PasskeyEndpointResults.Failure(PasskeyFailure.FlowExpired);
        browser.Clear(HttpContext);
        var result = await store.FinishRegistrationAsync(actor, flow, request.CredentialJson, origin, ct);
        if (!result.Succeeded) return PasskeyEndpointResults.Failure(result.FailureCode!.Value);
        HttpContext.Response.Cookies.Delete(RefreshCookie.Name(options.Value), RefreshCookie.DeleteOptions(options.Value));
        return TypedResults.Ok(result.Value!);
    }
}
