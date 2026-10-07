using FastEndpoints;
using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Options;
using NoCTF.API.Endpoints.Authentication.Mfa;
using NoCTF.API.Security;
using NoCTF.Application.Authentication.Passkeys;

namespace NoCTF.API.Endpoints.Authentication.Passkeys;

public sealed class CompletePasskeyLoginRequest { public string CredentialJson { get; set; } = string.Empty; }
public sealed class CompletePasskeyLoginValidator : Validator<CompletePasskeyLoginRequest>
{
    public CompletePasskeyLoginValidator() => RuleFor(value => value.CredentialJson).NotEmpty().MaximumLength(65536);
}
public sealed class CompletePasskeyLoginEndpoint(AuthenticateWithPasskey authenticate, PasskeyBrowserFlow browser,
    MfaBrowserFlow mfaBrowser, IOptions<RefreshHttpOptions> options) : Endpoint<CompletePasskeyLoginRequest, Results<Ok<AuthenticationResponse>, ProblemHttpResult>>
{
    public override void Configure()
    {
        Post("/auth/passkeys/login/complete"); AllowAnonymous(); MaxRequestBodySize(70 * 1024);
        Options(builder => builder.WithMetadata(new ProtectedEntryMetadata(ProtectedEntry.Authentication)));
    }
    public override async Task<Results<Ok<AuthenticationResponse>, ProblemHttpResult>> ExecuteAsync(CompletePasskeyLoginRequest request, CancellationToken ct)
    {
        HttpContext.Response.Headers.CacheControl = "private, no-store";
        var origin = PasskeyBrowserFlow.Origin(HttpContext.Request); var flow = browser.Read(HttpContext);
        if (origin is null) return PasskeyEndpointResults.Failure(PasskeyFailure.InvalidOrigin);
        if (flow is null) return PasskeyEndpointResults.Failure(PasskeyFailure.FlowExpired);
        browser.Clear(HttpContext);
        var result = await authenticate.ExecuteAsync(flow, request.CredentialJson, origin, mfaBrowser.Read(HttpContext), ct);
        return result.Succeeded ? TypedResults.Ok(AuthenticationResponseMapping.Map(result.Value!, HttpContext, options.Value, mfaBrowser))
            : PasskeyEndpointResults.Failure(result.FailureCode!.Value);
    }
}
