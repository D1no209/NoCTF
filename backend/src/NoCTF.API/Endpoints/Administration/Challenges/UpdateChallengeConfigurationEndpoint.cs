using FastEndpoints;
using FluentValidation;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Security;
using NoCTF.Application.Challenges.Configuration;
using NoCTF.Application.Teams.Moderation;

namespace NoCTF.API.Endpoints.Administration.Challenges;

public sealed class UpdateChallengeConfigurationRequest
{
    public string Json { get; set; } = string.Empty;
}

public sealed class UpdateChallengeConfigurationValidator
    : Validator<UpdateChallengeConfigurationRequest>
{
    public UpdateChallengeConfigurationValidator()
    {
        RuleFor(request => request.Json).NotEmpty();
    }
}

public sealed class UpdateChallengeConfigurationEndpoint(
    UpdateChallengeConfiguration update,
    ICompetitionModerationAuthorizer authorizer,
    IUserContext user)
    : Endpoint<UpdateChallengeConfigurationRequest,
        Results<Ok<ChallengeConfigurationResponse>, NotFound, ForbidHttpResult, ProblemHttpResult>>
{
    public override void Configure()
    {
        Put("/admin/competitions/{competitionId}/challenges/{competitionChallengeId}/configuration");
        AuthSchemes("Bearer");
        Description(builder => builder.WithName("AdminChallengeConfiguration_Update")
            .ProducesProblemFE(StatusCodes.Status409Conflict));
        Summary(summary =>
        {
            summary.Summary = "Updates a competition challenge's game-mode configuration.";
            summary.Description = "Validates and replaces challenge configuration JSON.";
        });
    }

    public override async Task<
        Results<Ok<ChallengeConfigurationResponse>, NotFound, ForbidHttpResult, ProblemHttpResult>> ExecuteAsync(
        UpdateChallengeConfigurationRequest request,
        CancellationToken ct)
    {
        var competitionId = Route<Guid>("competitionId");
        if (!await authorizer.CanModerateAsync(user.UserId, competitionId, ct))
            return TypedResults.Forbid();

        var result = await update.ExecuteAsync(
            competitionId,
            Route<Guid>("competitionChallengeId"),
            request.Json,
            DateTimeOffset.UtcNow,
            ct);
        if (result.FailureCode is ChallengeConfigurationFailureCode.CompetitionNotFound or ChallengeConfigurationFailureCode.ChallengeNotFound)
            return TypedResults.NotFound();
        if (!result.Succeeded)
        {
            var status = result.FailureCode is ChallengeConfigurationFailureCode.ConfigurationLocked
                ? StatusCodes.Status409Conflict
                : StatusCodes.Status400BadRequest;
            return TypedResults.Problem(
                statusCode: status,
                title: "Challenge configuration was not updated.",
                detail: result.ErrorMessage);
        }

        return TypedResults.Ok(ChallengeConfigurationMapping.ToResponse(result.Value!));
    }
}
