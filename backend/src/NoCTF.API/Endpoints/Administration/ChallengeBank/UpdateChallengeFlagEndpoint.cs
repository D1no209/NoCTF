using FastEndpoints;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Security;
using NoCTF.Application.Challenges.Flags;

namespace NoCTF.API.Endpoints.Administration.ChallengeBank;

public sealed class UpdateChallengeFlagEndpoint(
    ManageChallengeFlags flags,
    IUserContext user)
    : Endpoint<SaveChallengeFlagRequest, Results<Ok<ChallengeFlagResponse>, NotFound, ProblemHttpResult>>
{
    public override void Configure()
    {
        Put("/admin/challenges/{challengeId}/flags/{flagId}");
        AuthSchemes("Bearer");
        Description(builder => builder.WithName("AdminChallengeBankUpdateFlag"));
        Summary(summary =>
        {
            summary.Summary = "Updates a template-level static flag.";
            summary.Description = "Replaces protected flag material and validity metadata within the template scope.";
        });
    }

    public override async Task<Results<Ok<ChallengeFlagResponse>, NotFound, ProblemHttpResult>> ExecuteAsync(
        SaveChallengeFlagRequest request,
        CancellationToken ct)
    {
        var result = await flags.SaveAsync(
            SaveChallengeFlagMapping.ToCommand(
                request,
                ChallengeFlagScope.Template(Route<Guid>("challengeId")),
                Route<Guid>("flagId"),
                isCreate: false,
                DateTimeOffset.UtcNow),
            user.UserId,
            user.IsAdministrator,
            ct);
        if (result.FailureCode == ChallengeFlagFailureCode.FlagNotFound)
            return TypedResults.NotFound();
        return result.Succeeded
            ? TypedResults.Ok(ChallengeFlagMapping.ToResponse(result.Value!))
            : TypedResults.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "Flag was not updated.",
                detail: result.ErrorMessage);
    }
}
