using FastEndpoints;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Endpoints.Authentication.Mfa;
using NoCTF.Application.Authentication.Passkeys;

namespace NoCTF.API.Endpoints.Authentication.Passkeys;

public sealed class GetMyPasskeysEndpoint(IPasskeyStore store) : EndpointWithoutRequest<Results<Ok<PasskeyAccountStatus>, ProblemHttpResult>>
{
    public override void Configure() { Get("/auth/me/passkeys"); AuthSchemes("Bearer"); }
    public override async Task<Results<Ok<PasskeyAccountStatus>, ProblemHttpResult>> ExecuteAsync(CancellationToken ct)
    {
        HttpContext.Response.Headers.CacheControl = "private, no-store";
        var actor = MfaActorMapping.Read(User); var origin = PasskeyBrowserFlow.Origin(HttpContext.Request, true);
        if (actor is null) return PasskeyEndpointResults.Failure(PasskeyFailure.AccountUnavailable);
        var result = await store.ReadAccountAsync(actor, origin ?? string.Empty, ct);
        return result.Succeeded ? TypedResults.Ok(result.Value!) : PasskeyEndpointResults.Failure(result.FailureCode!.Value);
    }
}
