using FastEndpoints;
using FluentValidation;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using System.Text.Json.Serialization;
using NoCTF.API.Security;
using NoCTF.Application.Challenges.Bank;
using NoCTF.Domain.Challenges;
using NoCTF.Domain.Competitions;

namespace NoCTF.API.Endpoints.Administration.ChallengeBank;

public sealed class UpdateChallengeTemplateRequest
{
    [JsonConverter(typeof(JsonStringEnumConverter<GameMode>))]
    public GameMode Mode { get; set; }
    [JsonConverter(typeof(JsonStringEnumConverter<ChallengeVisibility>))]
    public ChallengeVisibility Visibility { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string Direction { get; set; } = string.Empty;
    public string DefinitionJson { get; set; } = """{"schemaVersion":1}""";
    public int ExpectedRevision { get; set; }
}

public sealed class UpdateChallengeTemplateValidator : Validator<UpdateChallengeTemplateRequest>
{
    public UpdateChallengeTemplateValidator()
    {
        RuleFor(request => request.Mode).IsInEnum();
        RuleFor(request => request.Visibility).IsInEnum();
        RuleFor(request => request.Title).NotEmpty().MaximumLength(160);
        RuleFor(request => request.Direction).NotEmpty().MaximumLength(96);
        RuleFor(request => request.DefinitionJson).NotEmpty();
        RuleFor(request => request.ExpectedRevision).GreaterThanOrEqualTo(0);
    }
}

public sealed class UpdateChallengeTemplateEndpoint(
    UpdateChallengeTemplate update,
    IUserContext user)
    : Endpoint<UpdateChallengeTemplateRequest,
        Results<Ok<ChallengeTemplateResponse>, NotFound, ProblemHttpResult>>
{
    public override void Configure()
    {
        Put("/admin/challenges/{challengeId}");
        AuthSchemes("Bearer");
        Description(builder => builder.WithName("AdminChallengeBankUpdateTemplate")
            .ProducesProblemFE(StatusCodes.Status409Conflict));
        Summary(summary =>
        {
            summary.Summary = "Updates global challenge metadata.";
            summary.Description = "Updates the reusable template and never changes competition-owned ordering or scoring.";
        });
    }

    public override async Task<Results<Ok<ChallengeTemplateResponse>, NotFound, ProblemHttpResult>> ExecuteAsync(
        UpdateChallengeTemplateRequest request,
        CancellationToken ct)
    {
        var current = await update.ExecuteAsync(new UpdateChallengeTemplateCommand(
            Route<Guid>("challengeId"),
            user.UserId,
            user.IsAdministrator,
            request.Mode,
            request.Visibility,
            request.Title,
            request.Description,
            request.Direction,
            request.DefinitionJson,
            request.ExpectedRevision,
            DateTimeOffset.UtcNow), ct);
        if (!current.Succeeded)
            return current.ErrorCode == "challenge_not_found"
                ? TypedResults.NotFound()
                : TypedResults.Problem(
                    statusCode: StatusCodes.Status409Conflict,
                    title: "Challenge template was not updated.",
                    detail: current.ErrorMessage);
        return TypedResults.Ok(ChallengeTemplateMapper.ToResponse(current.Value!));
    }
}
