using System.Text.Json.Serialization;
using FastEndpoints;
using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Security;
using NoCTF.API.Serialization;
using NoCTF.Application.LiveSolo.Adjudication;
using NoCTF.Application.LiveSolo.Rounds;
using NoCTF.Domain.LiveSolo;

namespace NoCTF.API.Endpoints.LiveSolo;

public sealed class AdjudicateLiveSoloMatchRequest
{
    public Guid CompetitionId { get; set; }
    public Guid MatchId { get; set; }
    public Guid ExpectedMatchStamp { get; set; }
    public Guid? ExpectedRoundId { get; set; }
    public Guid? ExpectedRoundStamp { get; set; }
    public long? ExpectedTimelineRevision { get; set; }
    [JsonConverter(typeof(StrictPascalCaseEnumConverter<LiveSoloJudgeAction>))] public required LiveSoloJudgeAction Action { get; set; }
    public Guid? ForfeitingTeamId { get; set; }
    public string Reason { get; set; } = "";
}
public sealed class AdjudicateLiveSoloMatchValidator : Validator<AdjudicateLiveSoloMatchRequest>
{
    public AdjudicateLiveSoloMatchValidator()
    {
        RuleFor(x => x.CompetitionId).NotEmpty(); RuleFor(x => x.MatchId).NotEmpty(); RuleFor(x => x.ExpectedMatchStamp).NotEmpty();
        RuleFor(x => x.Action).IsInEnum(); RuleFor(x => x.Reason).NotEmpty().MaximumLength(4000);
        RuleFor(x => x.ExpectedRoundStamp).NotEmpty().When(x => x.ExpectedRoundId.HasValue);
        RuleFor(x => x.ExpectedTimelineRevision).NotNull().GreaterThanOrEqualTo(0).When(x => x.ExpectedRoundId.HasValue);
        RuleFor(x => x.ForfeitingTeamId).NotEmpty().When(x => x.Action == LiveSoloJudgeAction.ForfeitMatch);
    }
}
public sealed record LiveSoloAdjudicationResponse(Guid Id, Guid MatchId, Guid? RoundId, Guid ActorUserId, Guid? ForfeitingTeamId,
    [property: JsonConverter(typeof(StrictPascalCaseEnumConverter<LiveSoloJudgeAction>))] LiveSoloJudgeAction Action,
    string Reason, DateTimeOffset OccurredAt,
    [property: JsonConverter(typeof(StrictPascalCaseEnumConverter<LiveSoloMatchState>))] LiveSoloMatchState PreviousMatchState,
    [property: JsonConverter(typeof(StrictPascalCaseEnumConverter<LiveSoloMatchState>))] LiveSoloMatchState MatchState,
    [property: JsonConverter(typeof(StrictPascalCaseEnumConverter<LiveSoloRoundState>))] LiveSoloRoundState? PreviousRoundState,
    [property: JsonConverter(typeof(StrictPascalCaseEnumConverter<LiveSoloRoundState>))] LiveSoloRoundState? RoundState,
    int PreviousLeftWins, int PreviousRightWins, int LeftWins, int RightWins, long? PreviousTimelineRevision, long? TimelineRevision)
{
    internal static LiveSoloAdjudicationResponse From(LiveSoloAdjudicationView x) => new(x.Id, x.MatchId, x.RoundId, x.ActorUserId, x.ForfeitingTeamId,
        x.Action, x.Reason, x.OccurredAt, x.PreviousMatchState, x.MatchState, x.PreviousRoundState, x.RoundState,
        x.PreviousLeftWins, x.PreviousRightWins, x.LeftWins, x.RightWins, x.PreviousTimelineRevision, x.TimelineRevision);
}
public sealed record AdjudicateLiveSoloMatchResponse(LiveSoloMatchResponse Match, LiveSoloRoundResponse? Round, LiveSoloAdjudicationResponse Decision);
public sealed class AdjudicateLiveSoloMatchEndpoint(ManageLiveSoloAdjudication adjudication, IUserContext user)
    : Endpoint<AdjudicateLiveSoloMatchRequest, Results<Ok<AdjudicateLiveSoloMatchResponse>, NotFound, ForbidHttpResult,
        Conflict<LiveSoloFailureResponse>, UnprocessableEntity<LiveSoloFailureResponse>, ProblemHttpResult>>
{
    public override void Configure()
    {
        Post("/competitions/{competitionId}/live-solo/matches/{matchId}/adjudications"); AuthSchemes("Bearer");
        Description(x => x.WithName("AdjudicateLiveSoloMatch"));
        Summary(x => x.Summary = "Applies a reasoned, revision-fenced judge action and records an immutable decision.");
    }
    public override async Task<Results<Ok<AdjudicateLiveSoloMatchResponse>, NotFound, ForbidHttpResult,
        Conflict<LiveSoloFailureResponse>, UnprocessableEntity<LiveSoloFailureResponse>, ProblemHttpResult>> ExecuteAsync(AdjudicateLiveSoloMatchRequest req, CancellationToken ct)
    {
        var result = await adjudication.ApplyAsync(new(req.CompetitionId, req.MatchId, user.UserId, req.ExpectedMatchStamp,
            req.ExpectedRoundId, req.ExpectedRoundStamp, req.ExpectedTimelineRevision, req.Action, req.ForfeitingTeamId, req.Reason), ct);
        return result.Failure switch
        {
            null when result.Match is not null && result.Decision is not null => TypedResults.Ok(new AdjudicateLiveSoloMatchResponse(
                LiveSoloProtocol.Match(result.Match), result.Round is null ? null : LiveSoloRoundProtocol.Round(result.Round),
                LiveSoloAdjudicationResponse.From(result.Decision))),
            LiveSoloFailure.NotFound => TypedResults.NotFound(),
            LiveSoloFailure.Forbidden => TypedResults.Forbid(),
            LiveSoloFailure.InvalidConfiguration => TypedResults.UnprocessableEntity(new LiveSoloFailureResponse(result.Failure.Value)),
            LiveSoloFailure.DependencyUnavailable => LiveSoloProtocol.Unavailable(result.Failure.Value),
            _ => TypedResults.Conflict(new LiveSoloFailureResponse(result.Failure ?? LiveSoloFailure.Conflict))
        };
    }
}
