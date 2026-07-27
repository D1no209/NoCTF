using FastEndpoints;
using FluentValidation;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Endpoints.Challenges;
using NoCTF.API.Security;
using NoCTF.Application.Challenges.Management;
using NoCTF.Application.Teams.Moderation;

namespace NoCTF.API.Endpoints.Administration.Challenges;

public sealed class UpdateChallengeRequest
{
    public long BaseScore { get; set; }
    public int Order { get; set; }
    public bool IsPublished { get; set; }
    public int ExpectedRevision { get; set; }
}

public sealed class UpdateChallengeValidator : Validator<UpdateChallengeRequest>
{
    public UpdateChallengeValidator()
    {
        RuleFor(request => request.BaseScore).GreaterThanOrEqualTo(0);
        RuleFor(request => request.Order).GreaterThanOrEqualTo(0);
        RuleFor(request => request.ExpectedRevision).GreaterThanOrEqualTo(0);
    }
}

public sealed class UpdateChallengeEndpoint(
    UpdateChallenge update,
    ICompetitionModerationAuthorizer authorizer,
    IUserContext user)
    : Endpoint<UpdateChallengeRequest,
        Results<Ok<ChallengeResponse>, NotFound, ForbidHttpResult, ProblemHttpResult>>
{
    public override void Configure()
    {
        Put("/admin/competitions/{competitionId}/challenges/{competitionChallengeId}");
        AuthSchemes("Bearer");
        Description(builder => builder.WithName("AdminUpdateCompetitionChallenge")
            .ProducesProblemFE(StatusCodes.Status409Conflict));
        Summary(summary =>
        {
            summary.Summary = "Updates a competition challenge.";
            summary.Description = "Updates scoring, ordering, and publication state using optimistic concurrency.";
        });
    }

    public override async Task<
        Results<Ok<ChallengeResponse>, NotFound, ForbidHttpResult, ProblemHttpResult>> ExecuteAsync(
        UpdateChallengeRequest request,
        CancellationToken ct)
    {
        var competitionId = Route<Guid>("competitionId");
        if (!await authorizer.CanModerateAsync(user.UserId, competitionId, ct))
            return TypedResults.Forbid();

        var result = await update.ExecuteAsync(new UpdateCompetitionChallengeCommand(
            competitionId,
            Route<Guid>("competitionChallengeId"),
            request.BaseScore,
            request.Order,
            request.IsPublished,
            request.ExpectedRevision,
            DateTimeOffset.UtcNow), ct);
        if (result.ErrorCode is "competition_not_found" or "competition_challenge_not_found")
            return TypedResults.NotFound();
        if (!result.Succeeded)
        {
            return TypedResults.Problem(
                statusCode: result.ErrorCode is "revision_conflict" or "challenge_order_conflict"
                    ? StatusCodes.Status409Conflict
                    : StatusCodes.Status400BadRequest,
                title: "Challenge was not updated.",
                detail: result.ErrorMessage);
        }

        return TypedResults.Ok(ChallengeMapper.ToResponse(result.Value!));
    }
}
