using FastEndpoints;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Security;
using NoCTF.Application.Challenges.Flags;

namespace NoCTF.API.Endpoints.Administration.ChallengeBank;

public sealed class RestoreChallengeFlagEndpoint(
    ManageChallengeFlags flags,
    IUserContext user)
    : EndpointWithoutRequest<Results<NoContent, NotFound>>
{
    public override void Configure()
    {
        Post("/admin/challenges/{challengeId}/flags/{flagId}/restore");
        AuthSchemes("Bearer");
        Description(builder => builder.WithName("AdminChallengeBankRestoreFlag"));
        Summary(summary =>
        {
            summary.Summary = "Restores a soft-deleted template flag.";
            summary.Description =
                "Restores protected static flag material within its original template scope.";
        });
    }

    public override async Task<Results<NoContent, NotFound>> ExecuteAsync(
        CancellationToken ct)
    {
        var result = await flags.RestoreAsync(
            ChallengeFlagScope.Template(Route<Guid>("challengeId")),
            Route<Guid>("flagId"),
            user.UserId,
            user.IsAdministrator,
            ct);
        return result.Succeeded ? TypedResults.NoContent() : TypedResults.NotFound();
    }
}
