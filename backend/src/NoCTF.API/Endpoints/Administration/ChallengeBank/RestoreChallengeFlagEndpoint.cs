using FastEndpoints;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Security;
using NoCTF.Application.Challenges.Flags;

namespace NoCTF.API.Endpoints.Administration.ChallengeBank;

public sealed class RestoreChallengeFlagEndpoint(
    ManageChallengeFlags flags,
    IUserContext user,
    TimeProvider timeProvider)
    : EndpointWithoutRequest<Results<NoContent, NotFound, Conflict<ChallengeFlagFailureResponse>>>
{
    public override void Configure()
    {
        Post("/admin/challenges/{challengeId}/flags/{flagId}/restore");
        AuthSchemes("Bearer");
        Description(builder => builder.WithName("AdminChallengeBankRestoreFlag"));
        Summary(summary =>
        {
            summary.Summary = "Restores a deleted template flag.";
            summary.Description = "Restores a soft-deleted template-scoped static flag by stable ID.";
        });
    }

    public override async Task<Results<NoContent, NotFound, Conflict<ChallengeFlagFailureResponse>>> ExecuteAsync(
        CancellationToken ct)
    {
        var result = await flags.RestoreAsync(
            ChallengeFlagScope.Template(Route<Guid>("challengeId")),
            Route<Guid>("flagId"),
            user.UserId,
            user.IsAdministrator,
            timeProvider.GetUtcNow(),
            ct);
        if (result.Succeeded)
            return TypedResults.NoContent();
        return result.FailureCode == ChallengeFlagFailureCode.SystemManagedFlag
            ? TypedResults.Conflict(ChallengeFlagFailureMapping.ToResponse(result.FailureCode.Value, result.ErrorMessage))
            : TypedResults.NotFound();
    }
}
