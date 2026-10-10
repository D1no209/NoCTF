using System.Text.Json.Serialization;
using FastEndpoints;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Security;
using NoCTF.API.Serialization;
using NoCTF.Application.LiveSolo.Adjudication;
using NoCTF.Application.LiveSolo.Rounds;
using NoCTF.Domain.LiveSolo;

namespace NoCTF.API.Endpoints.LiveSolo;

public sealed class GetLiveSoloResultCorrectionRequest { public Guid CompetitionId { get; set; } public Guid MatchId { get; set; } public Guid CorrectionId { get; set; } }
public sealed record LiveSoloCorrectionImpactResponse(Guid MatchId, Guid ConcurrencyStamp,
    [property: JsonConverter(typeof(StrictPascalCaseEnumConverter<LiveSoloMatchState>))] LiveSoloMatchState State,
    bool RequiresReplay, Guid? LeftTeamId, string? LeftTeamName, Guid? RightTeamId, string? RightTeamName, Guid? ReplacementMatchId)
{
    internal static LiveSoloCorrectionImpactResponse From(LiveSoloCorrectionImpact x) => new(x.MatchId,x.ConcurrencyStamp,x.State,x.RequiresReplay,
        x.LeftTeamId,x.LeftTeamName,x.RightTeamId,x.RightTeamName,x.ReplacementMatchId);
}
public sealed record LiveSoloResultCorrectionResponse(Guid Id, Guid MatchId, Guid ConcurrencyStamp,
    [property: JsonConverter(typeof(StrictPascalCaseEnumConverter<LiveSoloCorrectionState>))] LiveSoloCorrectionState State,
    Guid WinnerTeamId, int LeftWins, int RightWins, string Reason, DateTimeOffset CreatedAt, string? ResolutionReason,
    DateTimeOffset? ResolvedAt, IReadOnlyList<LiveSoloCorrectionImpactResponse> Downstream,
    Guid PreviousWinnerTeamId, int PreviousLeftWins, int PreviousRightWins, string ActorName, string? ResolvedByName)
{
    internal static LiveSoloResultCorrectionResponse From(LiveSoloCorrectionView x) => new(x.Id,x.MatchId,x.ConcurrencyStamp,x.State,x.WinnerTeamId,
        x.LeftWins,x.RightWins,x.Reason,x.CreatedAt,x.ResolutionReason,x.ResolvedAt,x.Downstream.Select(LiveSoloCorrectionImpactResponse.From).ToArray(),
        x.PreviousWinnerTeamId,x.PreviousLeftWins,x.PreviousRightWins,x.ActorName,x.ResolvedByName);
    internal static Results<Ok<LiveSoloResultCorrectionResponse>, NotFound, ForbidHttpResult, Conflict<LiveSoloFailureResponse>, UnprocessableEntity<LiveSoloFailureResponse>>
        Outcome(LiveSoloCorrectionResult result) => result.Failure switch
        {
            null when result.Correction is not null => TypedResults.Ok(From(result.Correction)),
            LiveSoloFailure.NotFound => TypedResults.NotFound(), LiveSoloFailure.Forbidden => TypedResults.Forbid(),
            LiveSoloFailure.InvalidConfiguration => TypedResults.UnprocessableEntity(new LiveSoloFailureResponse(result.Failure.Value)),
            _ => TypedResults.Conflict(new LiveSoloFailureResponse(result.Failure??LiveSoloFailure.Conflict))
        };
}
public sealed class GetLiveSoloResultCorrectionEndpoint(ILiveSoloResultCorrectionStore corrections, IUserContext user)
    : Endpoint<GetLiveSoloResultCorrectionRequest, Results<Ok<LiveSoloResultCorrectionResponse>, NotFound>>
{
    public override void Configure() { Get("/competitions/{competitionId}/live-solo/matches/{matchId}/corrections/{correctionId}"); AuthSchemes("Bearer");
        Description(x=>x.WithName("GetLiveSoloResultCorrection")); }
    public override async Task<Results<Ok<LiveSoloResultCorrectionResponse>, NotFound>> ExecuteAsync(GetLiveSoloResultCorrectionRequest req,CancellationToken ct)
    {
        HttpContext.Response.Headers.CacheControl="private, no-store";
        var result=await corrections.ReadCorrectionAsync(req.CompetitionId,req.MatchId,req.CorrectionId,user.UserId,ct);
        return result is null?TypedResults.NotFound():TypedResults.Ok(LiveSoloResultCorrectionResponse.From(result));
    }
}
