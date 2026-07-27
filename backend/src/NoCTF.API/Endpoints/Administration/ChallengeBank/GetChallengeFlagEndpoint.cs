using FastEndpoints;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Security;
using NoCTF.Application.Challenges.Flags;

namespace NoCTF.API.Endpoints.Administration.ChallengeBank;

public sealed class GetChallengeFlagEndpoint(
    ManageChallengeFlags flags,
    IUserContext user)
    : EndpointWithoutRequest<Results<Ok<ChallengeFlagResponse>, NotFound>>
{
    public override void Configure()
    {
        Get("/admin/challenges/{challengeId}/flags/{flagId}");
        AuthSchemes("Bearer");
        Description(builder => builder.WithName("AdminChallengeBankGetFlag"));
        Summary(summary =>
        {
            summary.Summary = "Gets a template-level static flag.";
            summary.Description = "Returns protected static flag material to an authorized template manager.";
        });
    }

    public override async Task<Results<Ok<ChallengeFlagResponse>, NotFound>> ExecuteAsync(
        CancellationToken ct)
    {
        var result = await flags.GetAsync(
            ChallengeFlagScope.Template(Route<Guid>("challengeId")),
            Route<Guid>("flagId"),
            user.UserId,
            user.IsAdministrator,
            ct);
        return result is null
            ? TypedResults.NotFound()
            : TypedResults.Ok(ChallengeFlagMapping.ToResponse(result));
    }
}
