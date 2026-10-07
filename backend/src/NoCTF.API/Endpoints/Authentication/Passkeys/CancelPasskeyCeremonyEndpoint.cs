using FastEndpoints;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.Application.Authentication.Passkeys;

namespace NoCTF.API.Endpoints.Authentication.Passkeys;

public sealed class CancelPasskeyCeremonyEndpoint(IPasskeyStore store, PasskeyBrowserFlow browser)
    : EndpointWithoutRequest<Results<NoContent, ProblemHttpResult>>
{
    public override void Configure() { Delete("/auth/passkeys/flow"); AllowAnonymous(); }
    public override async Task<Results<NoContent, ProblemHttpResult>> ExecuteAsync(CancellationToken ct)
    {
        var origin = PasskeyBrowserFlow.Origin(HttpContext.Request);
        if (origin is null || !store.AvailableForOrigin(origin)) return PasskeyEndpointResults.Failure(PasskeyFailure.InvalidOrigin);
        var flow = browser.Read(HttpContext); if (flow is not null) await store.CancelAsync(flow, ct);
        browser.Clear(HttpContext); return TypedResults.NoContent();
    }
}
