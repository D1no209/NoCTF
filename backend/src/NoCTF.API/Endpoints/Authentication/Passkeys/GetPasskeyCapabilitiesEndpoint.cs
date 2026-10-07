using FastEndpoints;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.Application.Authentication.Passkeys;

namespace NoCTF.API.Endpoints.Authentication.Passkeys;

public sealed record PasskeyCapabilitiesResponse(bool Available);
public static class PasskeyEndpointResults
{
    public static ProblemHttpResult Failure(PasskeyFailure failure) => TypedResults.Problem(statusCode: failure switch {
        PasskeyFailure.FlowExpired => 410, PasskeyFailure.CredentialNotFound => 404,
        PasskeyFailure.DependencyUnavailable => 503, PasskeyFailure.Unavailable => 503,
        PasskeyFailure.RateLimited => 429, PasskeyFailure.InvalidCredential or PasskeyFailure.InvalidName => 400, _ => 403 },
        title: "Passkey operation could not be completed.", extensions: new Dictionary<string, object?> { ["code"] = "Passkey" + failure });
}
public sealed class GetPasskeyCapabilitiesEndpoint(IPasskeyStore store) : EndpointWithoutRequest<Ok<PasskeyCapabilitiesResponse>>
{
    public override void Configure() { Get("/auth/passkeys/capabilities"); AllowAnonymous(); }
    public override Task<Ok<PasskeyCapabilitiesResponse>> ExecuteAsync(CancellationToken ct)
    {
        HttpContext.Response.Headers.CacheControl = "private, no-store";
        var origin = PasskeyBrowserFlow.Origin(HttpContext.Request, allowHeaderlessRead: true);
        return Task.FromResult(TypedResults.Ok(new PasskeyCapabilitiesResponse(origin is not null && store.AvailableForOrigin(origin))));
    }
}
