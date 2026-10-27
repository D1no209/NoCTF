using System.Text.Json.Serialization;
using FastEndpoints;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Security;
using NoCTF.API.Serialization;
using NoCTF.Application.LiveSolo.Matches;
using NoCTF.Domain.LiveSolo;

namespace NoCTF.API.Endpoints.LiveSolo;

public sealed class GetLiveSoloDirectionPoolRequest { public Guid CompetitionId { get; set; } public Guid MatchId { get; set; } public bool Staff { get; set; } }
public sealed record LiveSoloDirectionResponse(Guid Id, string Name, string Direction, int? RoundNumber, DateTimeOffset? DrawnAt,
    Guid? QuestionGroupId, bool Available);
public sealed record LiveSoloRescueResponse(Guid Id, Guid RoundId, Guid TeamId, Guid UserId, string UserName, string Reason,
    [property: JsonConverter(typeof(StrictPascalCaseEnumConverter<LiveSoloRescueState>))] LiveSoloRescueState State,
    DateTimeOffset RequestedAt, Guid? JudgeUserId, string? DecisionReason);
public sealed record LiveSoloDirectionPoolResponse(IReadOnlyList<LiveSoloDirectionResponse> Pool, IReadOnlyList<LiveSoloRescueResponse> Rescues,
    bool CanConfigure, bool CanDraw, bool CanRequestRescue, bool CanJudgeRescue, bool CanManagePresets);
public sealed class GetLiveSoloDirectionPoolEndpoint(ILiveSoloDirectionStore store, IUserContext user)
    : Endpoint<GetLiveSoloDirectionPoolRequest, Results<Ok<LiveSoloDirectionPoolResponse>, NotFound>>
{
    public override void Configure()
    {
        Get("/competitions/{competitionId}/live-solo/matches/{matchId}/direction-pool"); AuthSchemes("Bearer");
        Description(x => x.WithName("GetLiveSoloDirectionPool"));
    }
    public override async Task<Results<Ok<LiveSoloDirectionPoolResponse>, NotFound>> ExecuteAsync(GetLiveSoloDirectionPoolRequest req, CancellationToken ct)
    {
        HttpContext.Response.Headers.CacheControl = "private, no-store";
        var result = await store.DirectionsAsync(req.CompetitionId, req.MatchId, user.UserId, req.Staff, ct);
        return result is null ? TypedResults.NotFound() : TypedResults.Ok(new LiveSoloDirectionPoolResponse(
            result.Pool.Select(x => new LiveSoloDirectionResponse(x.Id, x.Name, x.Direction, x.RoundNumber, x.DrawnAt, x.QuestionGroupId, x.Available)).ToArray(),
            result.Rescues.Select(x => new LiveSoloRescueResponse(x.Id, x.RoundId, x.TeamId, x.UserId, x.UserName, x.Reason, x.State, x.RequestedAt, x.JudgeUserId, x.DecisionReason)).ToArray(),
            result.CanConfigure, result.CanDraw, result.CanRequestRescue, result.CanJudgeRescue, result.CanManagePresets));
    }
}
