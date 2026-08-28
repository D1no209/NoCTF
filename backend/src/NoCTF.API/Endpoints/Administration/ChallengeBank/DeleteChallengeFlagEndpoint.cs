using FastEndpoints;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Security;
using NoCTF.Application.Challenges.Flags;

namespace NoCTF.API.Endpoints.Administration.ChallengeBank;

public sealed class DeleteChallengeFlagEndpoint(
    ManageChallengeFlags flags,
    IUserContext user,
    TimeProvider timeProvider)
    : EndpointWithoutRequest<Results<NoContent, NotFound, Conflict<ChallengeFlagFailureResponse>>>
{
    public override void Configure()
    {
        Delete("/admin/challenges/{challengeId}/flags/{flagId}");
        AuthSchemes("Bearer");
        Description(builder => builder.WithName("AdminChallengeBankDeleteFlag"));
        Summary(summary =>
        {
            summary.Summary = "Deletes a template-level static flag.";
            summary.Description = "Soft-deletes protected static flag material from a global template.";
        });
    }

    public override async Task<Results<NoContent, NotFound, Conflict<ChallengeFlagFailureResponse>>> ExecuteAsync(CancellationToken ct)
    {
        var result = await flags.DeleteAsync(
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
