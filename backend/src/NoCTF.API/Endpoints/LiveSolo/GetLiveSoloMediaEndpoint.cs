using System.Text.Json.Serialization;
using FastEndpoints;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Security;
using NoCTF.API.Serialization;
using NoCTF.Application.LiveSolo.Media;
using NoCTF.Domain.LiveSolo;

namespace NoCTF.API.Endpoints.LiveSolo;

public sealed record LiveSoloMediaMemberResponse(Guid UserId, Guid TeamId,
    [property: JsonConverter(typeof(StrictPascalCaseEnumConverter<LiveSoloSide>))] LiveSoloSide Side,
    [property: JsonConverter(typeof(StrictPascalCaseEnumConverter<LiveSoloScreenState>))] LiveSoloScreenState State, string Identity, string UserName);
public sealed record LiveSoloMediaResponse(Guid Id, Guid MatchId, Guid Generation,
    [property: JsonConverter(typeof(StrictPascalCaseEnumConverter<LiveSoloMediaState>))] LiveSoloMediaState State,
    bool ParticipantsMayViewOpponents, IReadOnlyList<LiveSoloMediaMemberResponse> Members, int PublicDelaySeconds, bool RecordingEnabled,LiveSoloPublisherVideoPolicyResponse? VideoPolicy)
{
    internal static LiveSoloMediaResponse From(LiveSoloMediaView x) => new(x.Id, x.MatchId, x.Generation, x.State,
        x.ParticipantsMayViewOpponents, x.Members.Select(member => new LiveSoloMediaMemberResponse(member.UserId, member.TeamId, member.Side, member.State, member.Identity, member.UserName)).ToArray(),
        x.PublicDelaySeconds, x.RecordingEnabled,x.VideoPolicy is { } policy?new(policy.MaximumWidth,policy.MaximumHeight,policy.MaximumFramesPerSecond,policy.MaximumBitrateBitsPerSecond,policy.PolicyStamp):null);
}
public sealed record LiveSoloPublisherVideoPolicyResponse(int MaximumWidth,int MaximumHeight,int MaximumFramesPerSecond,int MaximumBitrateBitsPerSecond,Guid PolicyStamp);
public sealed class GetLiveSoloMediaRequest
{
    public Guid CompetitionId { get; set; }
    public Guid MatchId { get; set; }
}
public sealed class GetLiveSoloMediaEndpoint(ILiveSoloMediaStore store, IUserContext user)
    : Endpoint<GetLiveSoloMediaRequest, Results<Ok<LiveSoloMediaResponse>, NotFound>>
{
    public override void Configure()
    {
        Get("/competitions/{competitionId}/live-solo/matches/{matchId}/media"); AuthSchemes("Bearer");
        Description(x => x.WithName("GetLiveSoloMedia")); Summary(x => x.Summary = "Reads the current private media generation and roster sharing state.");
    }
    public override async Task<Results<Ok<LiveSoloMediaResponse>, NotFound>> ExecuteAsync(GetLiveSoloMediaRequest req, CancellationToken ct)
    {
        HttpContext.Response.Headers.CacheControl = "private, no-store";
        var result = await store.ReadAsync(req.CompetitionId, req.MatchId, user.UserId, ct);
        return result is null ? TypedResults.NotFound() : TypedResults.Ok(LiveSoloMediaResponse.From(result));
    }
}
