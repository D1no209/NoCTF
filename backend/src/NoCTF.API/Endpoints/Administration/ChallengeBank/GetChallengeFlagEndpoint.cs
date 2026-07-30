using FastEndpoints;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Security;
using NoCTF.Application.Challenges.Flags;

namespace NoCTF.API.Endpoints.Administration.ChallengeBank;

public sealed class GetChallengeFlagRequest
{
    public Guid ChallengeId { get; set; }
    public Guid FlagId { get; set; }
    [QueryParam]
    public bool IncludeDeleted { get; set; }
}

public sealed class GetChallengeFlagEndpoint(
    ManageChallengeFlags flags,
    IUserContext user)
    : Endpoint<GetChallengeFlagRequest, Results<Ok<ChallengeFlagResponse>, NotFound>>
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
        GetChallengeFlagRequest request,
        CancellationToken ct)
    {
        var result = await flags.GetAsync(
            ChallengeFlagScope.Template(request.ChallengeId),
            request.FlagId,
            user.UserId,
            user.IsAdministrator,
            request.IncludeDeleted,
            ct);
        return result is null
            ? TypedResults.NotFound()
            : TypedResults.Ok(ChallengeFlagMapping.ToResponse(result));
    }
}
