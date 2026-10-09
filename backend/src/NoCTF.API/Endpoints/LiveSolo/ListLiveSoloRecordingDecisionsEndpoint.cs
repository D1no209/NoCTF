using System.Text.Json.Serialization;
using FastEndpoints;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Security;
using NoCTF.API.Serialization;
using NoCTF.Application.LiveSolo.Media;
using NoCTF.Domain.LiveSolo;

namespace NoCTF.API.Endpoints.LiveSolo;

public sealed class ListLiveSoloRecordingDecisionsRequest
{
    public Guid CompetitionId { get; set; }
    public Guid MatchId { get; set; }
    public Guid RecordingId { get; set; }
}
public sealed record LiveSoloRecordingDecisionResponse(Guid Id, Guid RecordingId, Guid ActorUserId,
    [property: JsonConverter(typeof(StrictPascalCaseEnumConverter<LiveSoloRecordingAction>))] LiveSoloRecordingAction Action,
    string Reason, DateTimeOffset OccurredAt, bool PreviousHold, bool Hold, bool PreviousPublished, bool Published);
public sealed record LiveSoloRecordingDecisionsResponse(IReadOnlyList<LiveSoloRecordingDecisionResponse> Items);
public sealed class ListLiveSoloRecordingDecisionsEndpoint(ILiveSoloRecordingStore recordings, IUserContext user)
    : Endpoint<ListLiveSoloRecordingDecisionsRequest, Results<Ok<LiveSoloRecordingDecisionsResponse>, NotFound>>
{
    public override void Configure()
    {
        Get("/competitions/{competitionId}/live-solo/matches/{matchId}/recordings/{recordingId}/decisions"); AuthSchemes("Bearer");
        Description(x => x.WithName("ListLiveSoloRecordingDecisions"));
        Summary(x => x.Summary = "Reads immutable staff decisions, including records retained after recording cleanup.");
    }
    public override async Task<Results<Ok<LiveSoloRecordingDecisionsResponse>, NotFound>> ExecuteAsync(ListLiveSoloRecordingDecisionsRequest req, CancellationToken ct)
    {
        HttpContext.Response.Headers.CacheControl = "private, no-store";
        var result = await recordings.DecisionsAsync(req.CompetitionId, req.MatchId, req.RecordingId, user.UserId, ct);
        return result is null ? TypedResults.NotFound() : TypedResults.Ok(new LiveSoloRecordingDecisionsResponse(result.Select(x =>
            new LiveSoloRecordingDecisionResponse(x.Id, x.RecordingId, x.ActorUserId, x.Action, x.Reason, x.OccurredAt,
                x.PreviousHold, x.Hold, x.PreviousPublished, x.Published)).ToArray()));
    }
}
