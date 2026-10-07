using FastEndpoints;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using NoCTF.API.Endpoints.Authentication.Mfa;
using NoCTF.API.Security;
using NoCTF.Application.Authentication.Passkeys;

namespace NoCTF.API.Endpoints.Authentication.Passkeys;

public sealed class RemoveMyPasskeyRequest { [FromRoute] public Guid CredentialId { get; set; } }
public sealed class RemoveMyPasskeyEndpoint(IPasskeyStore store, MfaBrowserFlow mfaBrowser, IOptions<RefreshHttpOptions> options)
    : Endpoint<RemoveMyPasskeyRequest, Results<NoContent, ProblemHttpResult>>
{
    public override void Configure() { Delete("/auth/me/passkeys/{credentialId}"); AuthSchemes("Bearer"); }
    public override async Task<Results<NoContent, ProblemHttpResult>> ExecuteAsync(RemoveMyPasskeyRequest request, CancellationToken ct)
    {
        HttpContext.Response.Headers.CacheControl = "private, no-store";
        var actor = MfaActorMapping.Read(User); if (actor is null) return PasskeyEndpointResults.Failure(PasskeyFailure.AccountUnavailable);
        if (!RefreshRequestGuard.IsAllowed(HttpContext.Request, options.Value)) return PasskeyEndpointResults.Failure(PasskeyFailure.InvalidOrigin);
        var result = await store.RemoveAsync(actor, mfaBrowser.Read(HttpContext), request.CredentialId, ct);
        if (!result.Succeeded) return PasskeyEndpointResults.Failure(result.FailureCode!.Value);
        HttpContext.Response.Cookies.Delete(RefreshCookie.Name(options.Value), RefreshCookie.DeleteOptions(options.Value)); return TypedResults.NoContent();
    }
}
