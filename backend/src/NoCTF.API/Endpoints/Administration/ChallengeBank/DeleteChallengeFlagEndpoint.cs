using FastEndpoints;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Security;
using NoCTF.Application.Challenges.Flags;

namespace NoCTF.API.Endpoints.Administration.ChallengeBank;

public sealed class DeleteChallengeFlagEndpoint(
    ManageChallengeFlags flags,
    IUserContext user)
    : EndpointWithoutRequest<Results<NoContent, NotFound>>
{
    public override void Configure()
    {
        Delete("/admin/challenges/{challengeId}/flags/{flagId}");
        AuthSchemes("Bearer");
        Summary(summary => summary.Summary = "Soft-deletes a template-level static flag.");
    }

    public override async Task<Results<NoContent, NotFound>> ExecuteAsync(CancellationToken ct)
    {
        var result = await flags.DeleteAsync(
            ChallengeFlagScope.Template(Route<Guid>("challengeId")),
            Route<Guid>("flagId"),
            user.UserId,
            user.IsAdministrator,
            DateTimeOffset.UtcNow,
            ct);
        return result.Succeeded ? TypedResults.NoContent() : TypedResults.NotFound();
    }
}
