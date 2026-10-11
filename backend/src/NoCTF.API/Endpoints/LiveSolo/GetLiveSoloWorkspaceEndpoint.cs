using System.Text.Json.Serialization;
using FastEndpoints;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Security;
using NoCTF.API.Serialization;
using NoCTF.Application.LiveSolo.Matches;
using NoCTF.Domain.LiveSolo;

namespace NoCTF.API.Endpoints.LiveSolo;

public sealed class GetLiveSoloWorkspaceRequest
{
    public Guid CompetitionId { get; set; }
    public Guid MatchId { get; set; }
    public bool Staff { get; set; }
}
[JsonConverter(typeof(StrictPascalCaseEnumConverter<LiveSoloWorkspaceOperationProtocol>))]
public enum LiveSoloWorkspaceOperationProtocol { LockRoster, ConfirmReady, PrepareRound, StartCountdown, RetryPreparation, Pause, Resume, VoidRound, ForfeitMatch, CorrectResult, ManageQuestionPool, SubmitFlag }
[JsonConverter(typeof(StrictPascalCaseEnumConverter<LiveSoloStartConditionProtocol>))]
public enum LiveSoloStartConditionProtocol { CompetitionNotRunning, LeftRosterUnlocked, RightRosterUnlocked, LeftNotReady, RightNotReady, NoPreparedRound, EnvironmentPreparing, EnvironmentFailed, MediaRequired, SelectionChanged, CorrectionPending }
public sealed record LiveSoloPreparationResponse(Guid QuestionId, int Position, int OpenOffsetSeconds, DateTimeOffset? OpenedAt,
    [property: JsonConverter(typeof(StrictPascalCaseEnumConverter<LiveSoloQuestionReadiness>))] LiveSoloQuestionReadiness Readiness,
    [property: JsonConverter(typeof(StrictPascalCaseEnumConverter<LiveSoloResourceFailure>))] LiveSoloResourceFailure Failure,
    bool? EnvironmentRequired);
public sealed record LiveSoloWorkspaceResponse(LiveSoloMatchResponse Match, LiveSoloRoundResponse? Round,
    IReadOnlyList<LiveSoloWorkspaceOperationProtocol> AllowedOperations,
    IReadOnlyList<LiveSoloStartConditionProtocol> MissingConditions,
    IReadOnlyList<LiveSoloQuestionResponse> Questions, IReadOnlyList<LiveSoloReviewQuestionResponse> History,
    DateTimeOffset ServerTime, int? NextQuestionOffsetSeconds, bool Staff, IReadOnlyList<LiveSoloPreparationResponse> Resources);
public sealed class GetLiveSoloWorkspaceEndpoint(ILiveSoloWorkspaceReader workspace, IUserContext user, TimeProvider clock, LinkGenerator links)
    : Endpoint<GetLiveSoloWorkspaceRequest, Results<Ok<LiveSoloWorkspaceResponse>, NotFound>>
{
    public override void Configure()
    {
        Get("/competitions/{competitionId}/live-solo/matches/{matchId}/workspace"); AuthSchemes("Bearer");
        Description(x => x.WithName("GetLiveSoloWorkspace"));
    }
    public override async Task<Results<Ok<LiveSoloWorkspaceResponse>, NotFound>> ExecuteAsync(GetLiveSoloWorkspaceRequest req, CancellationToken ct)
    {
        HttpContext.Response.Headers.CacheControl = "private, no-store";
        var result = await workspace.ReadAsync(req.CompetitionId, req.MatchId, user.UserId, req.Staff, clock.GetUtcNow(), ct);
        return result is null ? TypedResults.NotFound() : TypedResults.Ok(new LiveSoloWorkspaceResponse(LiveSoloProtocol.Match(result.Match, links, HttpContext),
            result.Round != null ? LiveSoloRoundProtocol.Round(result.Round, result.Staff) : null, result.AllowedOperations.Select(x => (LiveSoloWorkspaceOperationProtocol)x).ToArray(), result.MissingConditions.Select(x => (LiveSoloStartConditionProtocol)x).ToArray(),
            result.Questions.Select(x => new LiveSoloQuestionResponse(x.Id, x.CompetitionChallengeId, x.Position, x.Title, x.Description, x.Direction, x.Tags, x.OpenedAt, x.RuntimeInstanceId)).ToArray(),
            result.History.Select(x => new LiveSoloReviewQuestionResponse(x.Id, x.RoundId, x.CompetitionChallengeId, x.RoundNumber, x.Replay, x.Position, x.Title, x.OpenedAt, x.ContentRecorded)).ToArray(),
            result.ServerTime, result.NextQuestionOffsetSeconds, result.Staff, result.Resources.Select(x => new LiveSoloPreparationResponse(
                x.QuestionId, x.Position, x.OpenOffsetSeconds, x.OpenedAt, x.Readiness, x.Failure, x.EnvironmentRequired)).ToArray()));
    }
}
