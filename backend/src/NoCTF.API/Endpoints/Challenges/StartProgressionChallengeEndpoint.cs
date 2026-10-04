using FastEndpoints;
using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Security;
using NoCTF.Application.Competitions.Progression;

namespace NoCTF.API.Endpoints.Challenges;

public sealed class StartProgressionChallengeRequest
{
    public Guid CompetitionId { get; set; }
    public Guid CompetitionChallengeId { get; set; }
    public long? ExpectedRevision { get; set; }
}

public sealed class StartProgressionChallengeValidator
    : Validator<StartProgressionChallengeRequest>
{
    public StartProgressionChallengeValidator()
    {
        RuleFor(request => request.CompetitionId).NotEmpty();
        RuleFor(request => request.CompetitionChallengeId).NotEmpty();
        RuleFor(request => request.ExpectedRevision)
            .GreaterThanOrEqualTo(0).When(request => request.ExpectedRevision.HasValue);
    }
}

[System.Text.Json.Serialization.JsonConverter(typeof(NoCTF.API.Serialization.StrictPascalCaseEnumConverter<ProgressionStartFailureCode>))]
public enum ProgressionStartFailureCode { GraphChanged }

public sealed record ProgressionStartConflict(ProgressionStartFailureCode Code, string Detail)
{
    public string Detail { get; init; } = ApiMessages.Localize(Code, Detail, ApiMessages.NoArguments);
    public string MessageKey => ApiMessages.For(Code).Key;
    public IReadOnlyDictionary<string, object?> MessageArguments => ApiMessages.NoArguments;
}

public sealed class StartProgressionChallengeEndpoint(
    IProgressionChallengeStarter starter, IUserContext user, TimeProvider clock)
    : Endpoint<StartProgressionChallengeRequest,
        Results<NoContent, NotFound, Conflict<ProgressionStartConflict>>>
{
    public override void Configure()
    {
        Summary(summary =>
        {
            summary.Summary = "Checks progression access and starts the selected competition challenge.";
            summary.Description = summary.Summary;
        });

        Post("/competitions/{competitionId}/challenges/{competitionChallengeId}/start");
        AuthSchemes("Bearer");
        Description(builder => builder.WithName("StartProgressionChallenge"));
    }

    public override async Task<Results<NoContent, NotFound, Conflict<ProgressionStartConflict>>> ExecuteAsync(
        StartProgressionChallengeRequest request, CancellationToken ct) =>
        await starter.StartAsync(request.CompetitionId, request.CompetitionChallengeId,
            user.UserId, request.ExpectedRevision, clock.GetUtcNow(), ct) switch
        {
            StartProgressionChallengeResult.Started => TypedResults.NoContent(),
            StartProgressionChallengeResult.GraphChanged => TypedResults.Conflict(
                new ProgressionStartConflict(ProgressionStartFailureCode.GraphChanged, "Progression rules changed; reload before starting.")),
            _ => TypedResults.NotFound()
        };
}
