using FastEndpoints;
using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Endpoints.Authentication.Mfa;
using NoCTF.API.Security;
using NoCTF.Application.Authentication.Passkeys;

namespace NoCTF.API.Endpoints.Authentication.Passkeys;

public sealed class BeginPasskeyRegistrationRequest { public string Name { get; set; } = string.Empty; }
public sealed class BeginPasskeyRegistrationValidator : Validator<BeginPasskeyRegistrationRequest>
{
    public BeginPasskeyRegistrationValidator() => RuleFor(value => value.Name).NotEmpty().MaximumLength(64);
}
public sealed class BeginPasskeyRegistrationEndpoint(IPasskeyStore store, PasskeyBrowserFlow browser, MfaBrowserFlow mfaBrowser)
    : Endpoint<BeginPasskeyRegistrationRequest, Results<Ok<PasskeyOptionsResponse>, ProblemHttpResult>>
{
    public override void Configure() { Post("/auth/me/passkeys/options"); AuthSchemes("Bearer"); MaxRequestBodySize(4096); }
    public override async Task<Results<Ok<PasskeyOptionsResponse>, ProblemHttpResult>> ExecuteAsync(BeginPasskeyRegistrationRequest request, CancellationToken ct)
    {
        HttpContext.Response.Headers.CacheControl = "private, no-store";
        var actor = MfaActorMapping.Read(User); var origin = PasskeyBrowserFlow.Origin(HttpContext.Request);
        if (actor is null) return PasskeyEndpointResults.Failure(PasskeyFailure.AccountUnavailable);
        if (origin is null) return PasskeyEndpointResults.Failure(PasskeyFailure.InvalidOrigin);
        var result = await store.BeginRegistrationAsync(actor, mfaBrowser.Read(HttpContext), request.Name, origin, ct);
        if (!result.Succeeded) return PasskeyEndpointResults.Failure(result.FailureCode!.Value);
        browser.Write(HttpContext, result.Value!); return TypedResults.Ok(PasskeyOptionsResponse.From(result.Value!));
    }
}
