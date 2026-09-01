using FastEndpoints;
using FluentValidation;
using FluentValidation.Results;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Composition;
using NoCTF.API.Endpoints.Competitions;
using NoCTF.API.Security;
using NoCTF.Application.Challenges.Bank;
using NoCTF.Domain.Challenges;
using NoCTF.Domain.Competitions;

namespace NoCTF.API.Endpoints.Administration.ChallengeBank;

public sealed class UpdateChallengeTemplateRequest
{
    public GameModeProtocol? Mode { get; set; }
    public ChallengeVisibilityProtocol? Visibility { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string Direction { get; set; } = string.Empty;
    public string DefinitionJson { get; set; } = string.Empty;
}

public sealed class UpdateChallengeTemplateValidator : Validator<UpdateChallengeTemplateRequest>
{
    public UpdateChallengeTemplateValidator()
    {
        RuleFor(request => request.Mode).NotNull().IsInEnum();
        RuleFor(request => request.Visibility).NotNull().IsInEnum();
        RuleFor(request => request.Title).NotEmpty().MaximumLength(160);
        RuleFor(request => request.Direction).NotEmpty().MaximumLength(96);
    }
}

public sealed class UpdateChallengeTemplateEndpoint(
    UpdateChallengeTemplate update,
    IUserContext user,
    TimeProvider timeProvider,
    ILogger<UpdateChallengeTemplateEndpoint> logger)
    : Endpoint<UpdateChallengeTemplateRequest,
        Results<
            Ok<ChallengeTemplateResponse>,
            NotFound,
            Conflict<ChallengeTemplateConflictResponse>,
            ProblemHttpResult>>
{
    public override void Configure()
    {
        Put("/admin/challenges/{challengeId}");
        AuthSchemes("Bearer");
        Description(builder => builder.WithName("AdminChallengeBankUpdateTemplate"));
        Summary(summary =>
        {
            summary.Summary = "Updates global challenge metadata.";
            summary.Description = "Updates the reusable template and never changes competition-owned ordering or scoring.";
        });
    }

    public override async Task<
        Results<
            Ok<ChallengeTemplateResponse>,
            NotFound,
            Conflict<ChallengeTemplateConflictResponse>,
            ProblemHttpResult>> ExecuteAsync(
        UpdateChallengeTemplateRequest request,
        CancellationToken ct)
    {
        var challengeId = Route<Guid>("challengeId");
        var result = await update.ExecuteAsync(new UpdateChallengeTemplateCommand(
            challengeId,
            user.UserId,
            user.IsAdministrator,
            CompetitionProtocolMapper.ToDomain(request.Mode!.Value),
            ChallengeTemplateMapper.ToDomain(request.Visibility!.Value),
            request.Title,
            request.Description,
            request.Direction,
            request.DefinitionJson,
            timeProvider.GetUtcNow()), ct);
        if (result.State is ChallengeTemplateWriteState.InvalidRequest
            or ChallengeTemplateWriteState.InvalidDefinition)
        {
            logger.LogWarning(
                "Challenge template {ChallengeId} update rejected for mode {Mode}: {State}. {Detail}",
                challengeId,
                request.Mode,
                result.State,
                result.Detail);
        }
        return ChallengeTemplateUpdateResponseMapper.ToResponse(result);
    }
}

public static class ChallengeTemplateUpdateResponseMapper
{
    public static Results<
        Ok<ChallengeTemplateResponse>,
        NotFound,
        Conflict<ChallengeTemplateConflictResponse>,
        ProblemHttpResult> ToResponse(
        ChallengeTemplateWriteResult result) =>
        result.State switch
        {
            ChallengeTemplateWriteState.Succeeded =>
                TypedResults.Ok(ChallengeTemplateMapper.ToResponse(result.Template!)),
            ChallengeTemplateWriteState.NotFoundOrForbidden =>
                TypedResults.NotFound(),
            ChallengeTemplateWriteState.ActiveCompetitionModeConflict =>
                TypedResults.Conflict(
                    ChallengeTemplateWriteResponseMapper.ToConflict(result)),
            ChallengeTemplateWriteState.InvalidRequest
                or ChallengeTemplateWriteState.InvalidDefinition =>
                TypedResults.Problem(ApiValidationProblemFactory.Create(
                    [
                        new ValidationFailure(
                            result.State == ChallengeTemplateWriteState.InvalidDefinition
                                ? nameof(UpdateChallengeTemplateRequest.DefinitionJson)
                                : "Request",
                            result.Detail ?? "Challenge template update is invalid.")
                    ],
                    StatusCodes.Status400BadRequest)),
            _ => throw new InvalidOperationException(
                $"Unsupported challenge template update state: {result.State}.")
        };
}
