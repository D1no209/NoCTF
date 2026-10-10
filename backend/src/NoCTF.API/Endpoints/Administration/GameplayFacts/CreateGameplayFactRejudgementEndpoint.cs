using FastEndpoints;
using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Security;
using NoCTF.API.Serialization;
using NoCTF.Application.GameplayFacts.Management;
using NoCTF.Application.GameplayFacts.Status;
using NoCTF.Application.Teams.Moderation;
using System.Text.Json.Serialization;

namespace NoCTF.API.Endpoints.Administration.GameplayFacts;

[JsonConverter(typeof(StrictPascalCaseEnumConverter<GameplayFactRejudgementTargetProtocol>))]
public enum GameplayFactRejudgementTargetProtocol
{
    GameplayFact,
    CompetitionChallenge
}

public sealed class CreateGameplayFactRejudgementRequest
{
    public GameplayFactRejudgementTargetProtocol? TargetKind { get; set; }
    public Guid TargetId { get; set; }
}

public sealed class CreateGameplayFactRejudgementValidator
    : Validator<CreateGameplayFactRejudgementRequest>
{
    public CreateGameplayFactRejudgementValidator()
    {
        RuleFor(request => request.TargetKind).NotNull().IsInEnum();
        RuleFor(request => request.TargetId).NotEmpty();
    }
}

public sealed record GameplayFactRejudgementResponse(
    GameplayFactRejudgementTargetProtocol TargetKind,
    Guid TargetId,
    DateTimeOffset Cutoff);

public sealed class CreateGameplayFactRejudgementEndpoint(
    QueueGameplayFactWork queue,
    IAdminGameplayFactStatusReader gameplayFacts,
    ICompetitionModerationAuthorizer authorizer,
    IUserContext user,
    TimeProvider timeProvider)
    : Endpoint<CreateGameplayFactRejudgementRequest,
        Results<Accepted<GameplayFactRejudgementResponse>, NotFound, ForbidHttpResult, ProblemHttpResult>>
{
    public override void Configure()
    {
        Post("/admin/competitions/{competitionId}/gameplay-fact-rejudgements");
        AuthSchemes("Bearer");
        Description(builder => builder.WithName("AdminCreateGameplayFactRejudgement"));
        Summary(summary =>
        {
            summary.Summary = "Queues an exact or challenge-scoped gameplay-fact rejudgement.";
            summary.Description = "Reuses existing gameplay facts and archives without consuming new attempts.";
        });
    }

    public override async Task<
        Results<Accepted<GameplayFactRejudgementResponse>, NotFound, ForbidHttpResult, ProblemHttpResult>> ExecuteAsync(
        CreateGameplayFactRejudgementRequest request,
        CancellationToken ct)
    {
        var competitionId = Route<Guid>("competitionId");
        if (!await authorizer.CanJudgeAsync(user.UserId, competitionId, ct))
            return TypedResults.Forbid();

        var cutoff = timeProvider.GetUtcNow();
        Guid competitionChallengeId;
        Guid? gameplayFactId;
        if (request.TargetKind == GameplayFactRejudgementTargetProtocol.GameplayFact)
        {
            var gameplayFact = await gameplayFacts.FindAsync(
                competitionId,
                request.TargetId,
                ct);
            if (gameplayFact is null)
                return TypedResults.NotFound();
            competitionChallengeId = gameplayFact.CompetitionChallengeId;
            gameplayFactId = request.TargetId;
        }
        else
        {
            competitionChallengeId = request.TargetId;
            gameplayFactId = null;
        }

        var queued = await queue.QueueAsync(
            competitionId,
            competitionChallengeId,
            cutoff,
            rejudge: true,
            gameplayFactId,
            ct);
        if (queued == GameplayFactWorkQueueState.NotFound) return TypedResults.NotFound();
        if (queued == GameplayFactWorkQueueState.IndependentAdjudicationRequired)
            return ApiProblems.Problem(statusCode: StatusCodes.Status422UnprocessableEntity,
                detail: ApiMessages.For(queued), extensions: new Dictionary<string, object?> { ["code"] = queued.ToString() });
        var response = new GameplayFactRejudgementResponse(
            request.TargetKind!.Value,
            request.TargetId,
            cutoff);
        return TypedResults.Accepted(uri: (string?)null, response);
    }
}
