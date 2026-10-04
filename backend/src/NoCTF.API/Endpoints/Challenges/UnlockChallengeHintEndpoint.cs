using FastEndpoints;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using System.Text.Json.Serialization;
using NoCTF.API.Endpoints.Administration.Challenges;
using NoCTF.API.Security;
using NoCTF.API.Endpoints.GameplayFacts;
using NoCTF.API.Serialization;
using NoCTF.Application.Challenges.Hints;

namespace NoCTF.API.Endpoints.Challenges;

[JsonConverter(typeof(StrictPascalCaseEnumConverter<ChallengeHintUnlockFailureCodeProtocol>))]
public enum ChallengeHintUnlockFailureCodeProtocol
{
    InsufficientScore
}

public sealed record ChallengeHintUnlockConflictResponse(
    ChallengeHintUnlockFailureCodeProtocol Code,
    string Detail)
{
    public string Detail { get; init; } = ApiMessages.Localize(Code, Detail, ApiMessages.NoArguments);
    public string MessageKey => ApiMessages.For(Code).Key;
    public IReadOnlyDictionary<string, object?> MessageArguments => ApiMessages.NoArguments;
}

public sealed class UnlockChallengeHintEndpoint(
    UnlockChallengeHint unlock,
    IUserContext user,
    TimeProvider timeProvider)
    : EndpointWithoutRequest<
        Results<Accepted<AcceptedGameplayFactResponse>, NotFound,
            Conflict<ChallengeHintUnlockConflictResponse>>>
{
    public override void Configure()
    {
        Post("/competitions/{competitionId}/challenges/{competitionChallengeId}/hints/{hintId}/unlock");
        AuthSchemes("Bearer");
        Summary(summary =>
        {
            summary.Summary = "Unlocks a published hint.";
            summary.Description = "The first paid unlock creates a HintUnlock fact from an authoritative database projection.";
        });
    }

    public override async Task<Results<Accepted<AcceptedGameplayFactResponse>, NotFound,
        Conflict<ChallengeHintUnlockConflictResponse>>> ExecuteAsync(
        CancellationToken ct)
    {
        var result = await unlock.ExecuteAsync(
            Route<Guid>("competitionId"),
            Route<Guid>("competitionChallengeId"),
            Route<Guid>("hintId"),
            user.UserId,
            timeProvider.GetUtcNow(),
            ct);
        if (result.FailureCode == ChallengeHintFailureCode.HintNotFound)
            return TypedResults.NotFound();
        if (!result.Succeeded)
            return TypedResults.Conflict(new ChallengeHintUnlockConflictResponse(
                ChallengeHintUnlockFailureCodeProtocol.InsufficientScore,
                result.ErrorMessage ?? "The hint could not be unlocked."));
        var statusUrl =
            $"/api/v1/competitions/{Route<Guid>("competitionId")}/gameplay-facts/{result.Value!.GameplayFactId}";
        return TypedResults.Accepted(
            uri: statusUrl,
            value: new AcceptedGameplayFactResponse(
                result.Value.GameplayFactId,
                GameplayFactStateProtocol.Queued,
                statusUrl));
    }
}
