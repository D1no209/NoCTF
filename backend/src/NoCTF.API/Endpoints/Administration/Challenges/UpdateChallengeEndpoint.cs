using FastEndpoints;
using FluentValidation;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Endpoints.Challenges;
using NoCTF.API.Security;
using NoCTF.Application.Challenges.Management;
using NoCTF.Application.Teams.Moderation;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace NoCTF.API.Endpoints.Administration.Challenges;

public sealed class UpdateChallengeRequest
{
    public required string? CustomTitle { get; set; }
    public long? BaseScore { get; set; }
    public int? Order { get; set; }
    public bool? IsPublished { get; set; }
}

public sealed class UpdateChallengeValidator : Validator<UpdateChallengeRequest>
{
    public UpdateChallengeValidator()
    {
        RuleFor(request => request.CustomTitle).MaximumLength(160);
        RuleFor(request => request.BaseScore).NotNull().GreaterThanOrEqualTo(0);
        RuleFor(request => request.Order).NotNull().GreaterThanOrEqualTo(0);
        RuleFor(request => request.IsPublished).NotNull();
    }
}

[JsonConverter(typeof(NoCTF.API.Serialization.StrictPascalCaseEnumConverter<CompetitionChallengeConflictCode>))]
public enum CompetitionChallengeConflictCode
{
    ResourceIdConflict,
    ChallengeOrderConflict,
    ChallengeTemplateConflict,
    LifecycleStateConflict,
    ChallengeTemplateNotFound,
    ChallengeTemplateModeMismatch
}

public sealed record CompetitionChallengeConflictResponse(
    [property: Required, JsonRequired] CompetitionChallengeConflictCode Code);

internal static class CompetitionChallengeConflictMapper
{
    public static CompetitionChallengeConflictResponse ToResponse(
        ChallengeMutationFailure failure) =>
        new(failure switch
        {
            ChallengeMutationFailure.ResourceIdConflict =>
                CompetitionChallengeConflictCode.ResourceIdConflict,
            ChallengeMutationFailure.ChallengeOrderConflict =>
                CompetitionChallengeConflictCode.ChallengeOrderConflict,
            ChallengeMutationFailure.ChallengeTemplateConflict =>
                CompetitionChallengeConflictCode.ChallengeTemplateConflict,
            ChallengeMutationFailure.LifecycleStateConflict =>
                CompetitionChallengeConflictCode.LifecycleStateConflict,
            ChallengeMutationFailure.TemplateNotFound =>
                CompetitionChallengeConflictCode.ChallengeTemplateNotFound,
            ChallengeMutationFailure.TemplateModeMismatch =>
                CompetitionChallengeConflictCode.ChallengeTemplateModeMismatch,
            _ => throw new InvalidOperationException(
                $"Unsupported competition challenge conflict: {failure}.")
        });
}

public sealed class UpdateChallengeEndpoint(
    UpdateChallenge update,
    ICompetitionModerationAuthorizer authorizer,
    IUserContext user)
    : Endpoint<UpdateChallengeRequest,
        Results<
            Ok<ChallengeResponse>,
            NotFound,
            ForbidHttpResult,
            Conflict<CompetitionChallengeConflictResponse>,
            ProblemHttpResult>>
{
    public override void Configure()
    {
        Put("/admin/competitions/{competitionId}/challenges/{competitionChallengeId}");
        AuthSchemes("Bearer");
        Description(builder => builder.WithName("AdminUpdateCompetitionChallenge"));
        Summary(summary =>
        {
            summary.Summary = "Updates a competition challenge.";
            summary.Description = "Updates scoring, ordering, and publication state.";
        });
    }

    public override async Task<
        Results<
            Ok<ChallengeResponse>,
            NotFound,
            ForbidHttpResult,
            Conflict<CompetitionChallengeConflictResponse>,
            ProblemHttpResult>> ExecuteAsync(
        UpdateChallengeRequest request,
        CancellationToken ct)
    {
        var competitionId = Route<Guid>("competitionId");
        if (!await authorizer.CanModerateAsync(user.UserId, competitionId, ct))
            return TypedResults.Forbid();

        var result = await update.ExecuteAsync(new UpdateCompetitionChallengeCommand(
            competitionId,
            Route<Guid>("competitionChallengeId"),
            request.BaseScore!.Value,
            request.Order!.Value,
            request.IsPublished!.Value,
            DateTimeOffset.UtcNow,
            request.CustomTitle), ct);
        if (result.Challenge is not null)
            return TypedResults.Ok(ChallengeMapper.ToResponse(result.Challenge));

        return result.Failure switch
        {
            ChallengeMutationFailure.CompetitionNotFound
                or ChallengeMutationFailure.ChallengeNotFound =>
                TypedResults.NotFound(),
            ChallengeMutationFailure.ChallengeOrderConflict =>
                TypedResults.Conflict(
                    CompetitionChallengeConflictMapper.ToResponse(result.Failure.Value)),
            ChallengeMutationFailure.InvalidBaseScore
                or ChallengeMutationFailure.InvalidTitle
                or ChallengeMutationFailure.InvalidOrder =>
                TypedResults.Problem(
                    statusCode: StatusCodes.Status400BadRequest,
                    title: "Challenge was not updated.",
                    detail: "Competition challenge update values are invalid."),
            _ => throw new InvalidOperationException(
                $"Unsupported competition challenge update failure: {result.Failure}.")
        };
    }
}
