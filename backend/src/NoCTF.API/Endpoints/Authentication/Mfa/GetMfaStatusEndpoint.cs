using System.Security.Claims;
using FastEndpoints;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.Application.Authentication.Mfa;
using NoCTF.Domain.Identity;
using NoCTF.Infrastructure.Authentication.Mfa;

namespace NoCTF.API.Endpoints.Authentication.Mfa;

public sealed record MfaStatusResponse(bool Enrolled, bool Required, bool Mandated, int RecoveryCodesRemaining, bool RecoveryMailAvailable, bool RecentPrimaryAuthentication);

public static class MfaActorMapping
{
    public static MfaActor? Read(ClaimsPrincipal principal)
    {
        var id = principal.FindFirstValue(ClaimTypes.NameIdentifier) ?? principal.FindFirstValue("sub");
        var authentication = AuthenticationContextClaims.Read(principal);
        return Guid.TryParse(id, out var userId) && int.TryParse(principal.FindFirstValue("token_version"), out var version) && authentication is not null
            ? new(userId, version, authentication) : null;
    }
}

public sealed class GetMfaStatusEndpoint(IMfaAuthenticationStore mfa, TimeProvider clock) : EndpointWithoutRequest<Results<Ok<MfaStatusResponse>, ProblemHttpResult>>
{
    public override void Configure() { Get("/auth/mfa/status"); AuthSchemes("Bearer"); }
    public override async Task<Results<Ok<MfaStatusResponse>, ProblemHttpResult>> ExecuteAsync(CancellationToken ct)
    {
        HttpContext.Response.Headers.CacheControl = "private, no-store";
        var actor = MfaActorMapping.Read(User);
        if (actor is null) return MfaEndpointResults.Failure(MfaFailure.NotApplicable);
        var snapshot = await mfa.ReadAccountAsync(actor.UserId, ct);
        if (snapshot is null || snapshot.User.Kind != UserKind.Human) return MfaEndpointResults.Failure(MfaFailure.NotApplicable);
        return TypedResults.Ok(new MfaStatusResponse(snapshot.CredentialId is not null, snapshot.Required, snapshot.Mandated,
            snapshot.RecoveryCodesRemaining, snapshot.RecoveryMailAvailable, actor.Authentication.IsInteractive && clock.GetUtcNow() - actor.Authentication.AuthenticatedAt <= TimeSpan.FromMinutes(5)));
    }
}
