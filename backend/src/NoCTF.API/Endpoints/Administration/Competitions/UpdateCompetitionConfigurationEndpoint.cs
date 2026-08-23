using FastEndpoints;
using FluentValidation;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Security;
using NoCTF.Application.Competitions.Configuration;
using NoCTF.Application.Teams.Moderation;

namespace NoCTF.API.Endpoints.Administration.Competitions;

public sealed class UpdateCompetitionConfigurationRequest
{
    public string Json { get; set; } = string.Empty;
}

public sealed class UpdateCompetitionConfigurationValidator
    : Validator<UpdateCompetitionConfigurationRequest>
{
    public UpdateCompetitionConfigurationValidator()
    {
        RuleFor(request => request.Json).NotEmpty();
    }
}

public sealed class UpdateCompetitionConfigurationEndpoint(
    UpdateCompetitionConfiguration update,
    ICompetitionModerationAuthorizer authorizer,
    IUserContext user)
    : Endpoint<UpdateCompetitionConfigurationRequest,
        Results<Ok<CompetitionConfigurationResponse>, NotFound, ForbidHttpResult, ProblemHttpResult>>
{
    public override void Configure()
    {
        Put("/admin/competitions/{competitionId}/configuration");
        AuthSchemes("Bearer");
        Description(builder => builder.WithName("AdminCompetitionConfiguration_Update")
            .ProducesProblemFE(StatusCodes.Status409Conflict));
        Summary(summary =>
        {
            summary.Summary = "Updates a competition's game-mode configuration.";
            summary.Description = "Validates and replaces the competition configuration JSON.";
        });
    }

    public override async Task<
        Results<Ok<CompetitionConfigurationResponse>, NotFound, ForbidHttpResult, ProblemHttpResult>> ExecuteAsync(
        UpdateCompetitionConfigurationRequest request,
        CancellationToken ct)
    {
        var competitionId = Route<Guid>("competitionId");
        if (!await authorizer.CanModerateAsync(user.UserId, competitionId, ct))
            return TypedResults.Forbid();

        var result = await update.ExecuteAsync(
            competitionId,
            request.Json,
            DateTimeOffset.UtcNow,
            ct);
        if (result.FailureCode == CompetitionConfigurationFailureCode.CompetitionNotFound)
            return TypedResults.NotFound();
        if (!result.Succeeded)
        {
            var status = result.FailureCode == CompetitionConfigurationFailureCode.ConfigurationLocked
                ? StatusCodes.Status409Conflict
                : StatusCodes.Status400BadRequest;
            return TypedResults.Problem(
                statusCode: status,
                title: "Competition configuration was not updated.",
                detail: result.ErrorMessage);
        }

        return TypedResults.Ok(CompetitionConfigurationMapping.ToResponse(result.Value!));
    }
}
