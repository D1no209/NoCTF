using FastEndpoints;
using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Endpoints.GameplayFacts;
using NoCTF.API.Security;
using NoCTF.Application.GameplayFacts.Intake;
using NoCTF.Application.Teams.Moderation;

namespace NoCTF.API.Endpoints.Administration.GameplayFacts;

public sealed class CreateManualAdjustmentRequest
{
    public Guid TeamId { get; set; }
    public Guid CompetitionChallengeId { get; set; }
    public int Delta { get; set; }
}

public sealed class CreateManualAdjustmentValidator : Validator<CreateManualAdjustmentRequest>
{
    public CreateManualAdjustmentValidator()
    {
        RuleFor(request => request.TeamId).NotEmpty();
        RuleFor(request => request.CompetitionChallengeId).NotEmpty();
        RuleFor(request => request.Delta).NotEqual(0);
    }
}

public sealed class CreateManualAdjustmentEndpoint(
    CreateManualAdjustment create,
    ICompetitionModerationAuthorizer authorizer,
    IUserContext user,
    TimeProvider timeProvider)
    : Endpoint<CreateManualAdjustmentRequest,
        Results<Accepted<AcceptedGameplayFactResponse>, ForbidHttpResult, ProblemHttpResult>>
{
    public override void Configure()
    {
        Post("/admin/competitions/{competitionId}/gameplay-facts/manual-adjustments");
        AuthSchemes("Bearer");
        Options(builder => builder.WithMetadata(new NoCTF.API.Security.ProtectedEntryMetadata(NoCTF.API.Security.ProtectedEntry.ManualAdjustment)));
        Description(builder => builder.WithName("AdminCreateManualAdjustment"));
        Summary(summary =>
        {
            summary.Summary = "Records a signed manual score adjustment.";
            summary.Description = "The delta is persisted as the canonical signed Int32 Value on a completed ManualAdjustment gameplay fact.";
        });
    }

    public override async Task<Results<Accepted<AcceptedGameplayFactResponse>, ForbidHttpResult, ProblemHttpResult>> ExecuteAsync(
        CreateManualAdjustmentRequest request,
        CancellationToken ct)
    {
        var competitionId = Route<Guid>("competitionId");
        if (!await authorizer.CanJudgeAsync(user.UserId, competitionId, ct))
            return TypedResults.Forbid();
        var result = await create.ExecuteAsync(new(
            competitionId,
            request.TeamId,
            request.CompetitionChallengeId,
            request.Delta,
            user.UserId,
            timeProvider.GetUtcNow()), ct);
        if (!result.Succeeded)
            return ApiProblems.Problem(
                statusCode: StatusCodes.Status404NotFound,
                title: ApiMessages.Get(ApiMessageId.CreateManualAdjustmentTitleManualAdjustmentWasAccepted),
                detail: ApiMessages.Get(ApiMessageId.CreateManualAdjustmentTitleManualAdjustmentWasAccepted));
        var accepted = result.Value!;
        var statusUrl =
            $"/api/v1/competitions/{competitionId}/gameplay-facts/{accepted.GameplayFactId}";
        return TypedResults.Accepted(
            uri: statusUrl,
            value: new AcceptedGameplayFactResponse(
                accepted.GameplayFactId,
                GameplayFactStateProtocol.Completed,
                statusUrl));
    }
}
