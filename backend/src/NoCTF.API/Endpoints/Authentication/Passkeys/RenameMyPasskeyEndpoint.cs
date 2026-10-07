using FastEndpoints;
using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using NoCTF.API.Endpoints.Authentication.Mfa;
using NoCTF.API.Security;
using NoCTF.Application.Authentication.Passkeys;

namespace NoCTF.API.Endpoints.Authentication.Passkeys;

public sealed class RenameMyPasskeyRequest { [FromRoute] public Guid CredentialId { get; set; } public string Name { get; set; } = string.Empty; }
public sealed class RenameMyPasskeyValidator : Validator<RenameMyPasskeyRequest>
{
    public RenameMyPasskeyValidator() { RuleFor(value => value.CredentialId).NotEmpty(); RuleFor(value => value.Name).NotEmpty().MaximumLength(64); }
}
public sealed class RenameMyPasskeyEndpoint(IPasskeyStore store, MfaBrowserFlow mfaBrowser, Microsoft.Extensions.Options.IOptions<RefreshHttpOptions> options)
    : Endpoint<RenameMyPasskeyRequest, Results<Ok<PasskeyAccountCredential>, ProblemHttpResult>>
{
    public override void Configure() { Patch("/auth/me/passkeys/{credentialId}"); AuthSchemes("Bearer"); MaxRequestBodySize(4096); }
    public override async Task<Results<Ok<PasskeyAccountCredential>, ProblemHttpResult>> ExecuteAsync(RenameMyPasskeyRequest request, CancellationToken ct)
    {
        HttpContext.Response.Headers.CacheControl = "private, no-store";
        var actor = MfaActorMapping.Read(User); if (actor is null) return PasskeyEndpointResults.Failure(PasskeyFailure.AccountUnavailable);
        if (!RefreshRequestGuard.IsAllowed(HttpContext.Request, options.Value)) return PasskeyEndpointResults.Failure(PasskeyFailure.InvalidOrigin);
        var result = await store.RenameAsync(actor, mfaBrowser.Read(HttpContext), request.CredentialId, request.Name, ct);
        return result.Succeeded ? TypedResults.Ok(result.Value!) : PasskeyEndpointResults.Failure(result.FailureCode!.Value);
    }
}
