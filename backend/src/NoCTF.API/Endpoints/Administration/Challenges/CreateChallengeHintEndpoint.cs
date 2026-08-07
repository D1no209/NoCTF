using FastEndpoints;
using FluentValidation;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Security;
using NoCTF.Application.Challenges.Hints;
using NoCTF.Application.Teams.Moderation;

namespace NoCTF.API.Endpoints.Administration.Challenges;

public sealed class SaveChallengeHintRequest
{
    public Guid? Id { get; set; }
    public string Content { get; set; } = string.Empty;
    public long Cost { get; set; }
    public DateTimeOffset? PublishedAt { get; set; }
}

public sealed class SaveChallengeHintValidator : Validator<SaveChallengeHintRequest>
{
    public SaveChallengeHintValidator()
    {
        RuleFor(request => request.Id)
            .Must(id => id is null || id != Guid.Empty)
            .WithMessage("Id cannot be empty when supplied.");
        RuleFor(request => request.Content).NotEmpty();
        RuleFor(request => request.Cost).GreaterThanOrEqualTo(0);
    }
}

public sealed class CreateChallengeHintEndpoint(
    ManageChallengeHints hints,
    ICompetitionModerationAuthorizer authorizer,
    IUserContext user)
    : Endpoint<SaveChallengeHintRequest,
        Results<Created<ChallengeHintResponse>, NotFound, ForbidHttpResult, ProblemHttpResult>>
{
    public override void Configure()
    {
        Post("/admin/competitions/{competitionId}/challenges/{competitionChallengeId}/hints");
        AuthSchemes("Bearer");
        Description(builder => builder.WithName("AdminCreateCompetitionChallengeHint"));
        Summary(summary =>
        {
            summary.Summary = "Creates a competition challenge hint.";
            summary.Description = "Creates hint content, cost, and optional publication timing for one challenge instance.";
        });
    }

    public override async Task<Results<Created<ChallengeHintResponse>, NotFound, ForbidHttpResult, ProblemHttpResult>> ExecuteAsync(
        SaveChallengeHintRequest request,
        CancellationToken ct)
    {
        var competitionId = Route<Guid>("competitionId");
        if (!await authorizer.CanModerateAsync(user.UserId, competitionId, ct))
            return TypedResults.Forbid();
        var challengeId = Route<Guid>("competitionChallengeId");
        var result = await hints.SaveAsync(new(
            competitionId, challengeId, request.Id, true, request.Content, request.Cost,
            request.PublishedAt, DateTimeOffset.UtcNow), ct);
        if (result.FailureCode == ChallengeHintFailureCode.HintNotFound)
            return TypedResults.NotFound();
        if (!result.Succeeded)
            return TypedResults.Problem(
                statusCode: result.FailureCode == ChallengeHintFailureCode.ResourceIdConflict
                    ? StatusCodes.Status409Conflict
                    : StatusCodes.Status400BadRequest,
                title: "Hint was not created.",
                detail: result.ErrorMessage,
                extensions: new Dictionary<string, object?> { ["code"] = result.FailureCode?.ToString() });
        var response = ChallengeHintMapping.ToResponse(result.Value!);
        return TypedResults.Created(
            $"/api/v1/admin/competitions/{competitionId}/challenges/{challengeId}/hints/{response.Id}",
            response);
    }
}
