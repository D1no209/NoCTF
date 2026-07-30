using FastEndpoints;
using FluentValidation;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Endpoints.Challenges;
using NoCTF.API.Security;
using NoCTF.Application.Challenges.Management;
using NoCTF.Application.Teams.Moderation;

namespace NoCTF.API.Endpoints.Administration.Challenges;

public sealed class CreateChallengeRequest
{
    public Guid? Id { get; set; }
    public Guid ChallengeId { get; set; }
    public long BaseScore { get; set; }
    public int Order { get; set; }
}

public sealed class CreateChallengeValidator : Validator<CreateChallengeRequest>
{
    public CreateChallengeValidator()
    {
        RuleFor(request => request.ChallengeId).NotEmpty();
        RuleFor(request => request.Id)
            .NotEqual(Guid.Empty)
            .When(request => request.Id is not null);
        RuleFor(request => request.BaseScore).GreaterThanOrEqualTo(0);
        RuleFor(request => request.Order).GreaterThanOrEqualTo(0);
    }
}

public sealed class CreateChallengeEndpoint(
    CreateChallenge create,
    ICompetitionModerationAuthorizer authorizer,
    IUserContext user)
    : Endpoint<CreateChallengeRequest,
        Results<Created<ChallengeResponse>, NotFound, ForbidHttpResult, ProblemHttpResult>>
{
    public override void Configure()
    {
        Post("/admin/competitions/{competitionId}/challenges");
        AuthSchemes("Bearer");
        Description(builder => builder.WithName("AdminCreateCompetitionChallenge")
            .ProducesProblemFE(StatusCodes.Status409Conflict));
        Summary(summary =>
        {
            summary.Summary = "Links a global challenge template to a competition.";
            summary.Description = "Creates a CompetitionChallenge without copying or mutating the global template.";
        });
    }

    public override async Task<
        Results<Created<ChallengeResponse>, NotFound, ForbidHttpResult, ProblemHttpResult>> ExecuteAsync(
        CreateChallengeRequest request,
        CancellationToken ct)
    {
        var competitionId = Route<Guid>("competitionId");
        if (!await authorizer.CanModerateAsync(user.UserId, competitionId, ct))
            return TypedResults.Forbid();

        var result = await create.ExecuteAsync(new CreateCompetitionChallengeCommand(
            competitionId,
            request.ChallengeId,
            request.BaseScore,
            request.Order,
            DateTimeOffset.UtcNow,
            request.Id), ct);
        if (result.ErrorCode is "competition_not_found" or "challenge_template_not_found")
            return TypedResults.NotFound();
        if (!result.Succeeded)
        {
            return TypedResults.Problem(
                statusCode: result.ErrorCode == "challenge_order_conflict"
                    ? StatusCodes.Status409Conflict
                    : StatusCodes.Status400BadRequest,
                title: "Challenge was not created.",
                detail: result.ErrorMessage);
        }

        var response = ChallengeMapper.ToResponse(result.Value!);
        return TypedResults.Created(
            $"/api/v1/admin/competitions/{competitionId}/challenges/{response.Id}",
            response);
    }
}
