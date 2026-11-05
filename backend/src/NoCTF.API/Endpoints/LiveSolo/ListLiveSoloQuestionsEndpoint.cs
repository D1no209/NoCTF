using FastEndpoints;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Security;
using NoCTF.Application.LiveSolo.Matches;
using NoCTF.Application.LiveSolo.Rounds;
using NoCTF.Application.Challenges.Actions;
using NoCTF.Domain.LiveSolo;
using NoCTF.API.Serialization;
using System.Text.Json.Serialization;

namespace NoCTF.API.Endpoints.LiveSolo;

public sealed class ListLiveSoloQuestionsRequest
{
    public Guid CompetitionId { get; set; }
    public Guid MatchId { get; set; }
    public Guid RoundId { get; set; }
}
public sealed record LiveSoloQuestionResponse(Guid Id, Guid CompetitionChallengeId, int Position, string Title, string? Description,
    string Direction, IReadOnlyList<string> Tags, DateTimeOffset OpenedAt, Guid? RuntimeInstanceId)
{
    [JsonConverter(typeof(StrictPascalCaseEnumConverter<LiveSoloCompletionKind>))]
    public LiveSoloCompletionKind CompletionKind { get; init; }
    public IReadOnlyList<LiveSoloAvailableActionResponse> AvailableActions { get; init; } = [];
    public static LiveSoloQuestionResponse From(LiveSoloQuestionView value) => new(value.Id, value.CompetitionChallengeId,
        value.Position, value.Title, value.Description, value.Direction, value.Tags, value.OpenedAt, value.RuntimeInstanceId)
    {
        CompletionKind = value.CompletionKind,
        AvailableActions = value.AvailableActions.Select(action => new LiveSoloAvailableActionResponse(action.Kind, action.Enabled,
            action.Availability, action.Failure, action.RuntimeInstanceId, action.GameplayFactId)).ToArray()
    };
}
public sealed record LiveSoloAvailableActionResponse(
    [property: JsonConverter(typeof(StrictPascalCaseEnumConverter<ChallengeActionKind>))] ChallengeActionKind Kind,
    bool Enabled,
    [property: JsonConverter(typeof(StrictPascalCaseEnumConverter<ChallengeActionAvailability>))] ChallengeActionAvailability Availability,
    [property: JsonConverter(typeof(StrictPascalCaseEnumConverter<LiveSoloFailure>))] LiveSoloFailure? Failure,
    Guid? RuntimeInstanceId, Guid? GameplayFactId);
public sealed record LiveSoloQuestionsResponse(IReadOnlyList<LiveSoloQuestionResponse> Items);
public sealed class ListLiveSoloQuestionsEndpoint(ILiveSoloMatchStore matches, IUserContext user, TimeProvider clock)
    : Endpoint<ListLiveSoloQuestionsRequest, Results<Ok<LiveSoloQuestionsResponse>, NotFound>>
{
    public override void Configure()
    {
        Get("/competitions/{competitionId}/live-solo/matches/{matchId}/rounds/{roundId}/questions"); AuthSchemes("Bearer");
        Description(x => x.WithName("ListLiveSoloQuestions"));
        Summary(x => x.Summary = "Lists only actually opened questions for an eligible locked roster member in the current Round.");
    }
    public override async Task<Results<Ok<LiveSoloQuestionsResponse>, NotFound>> ExecuteAsync(ListLiveSoloQuestionsRequest req, CancellationToken ct)
    {
        HttpContext.Response.Headers.CacheControl = "private, no-store";
        var result = await matches.QuestionsAsync(req.CompetitionId, req.MatchId, req.RoundId, user.UserId, clock.GetUtcNow(), ct);
        return result is null ? TypedResults.NotFound() : TypedResults.Ok(new LiveSoloQuestionsResponse(result.Select(LiveSoloQuestionResponse.From).ToArray()));
    }
}
