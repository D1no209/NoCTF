using System.Text.Json.Serialization;
using FastEndpoints;
using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Security;
using NoCTF.API.Serialization;
using NoCTF.Application.LiveSolo.Media;
using NoCTF.Domain.LiveSolo;

namespace NoCTF.API.Endpoints.LiveSolo;

public sealed class ListLiveSoloRecordingsRequest
{
    public Guid CompetitionId { get; set; }
    public Guid MatchId { get; set; }
    public bool Staff { get; set; }
    public int Offset { get; set; }
    public int Limit { get; set; } = 20;
}
public sealed class ListLiveSoloRecordingsValidator : Validator<ListLiveSoloRecordingsRequest>
{
    public ListLiveSoloRecordingsValidator()
    {
        RuleFor(x => x.CompetitionId).NotEmpty(); RuleFor(x => x.MatchId).NotEmpty();
        RuleFor(x => x.Offset).GreaterThanOrEqualTo(0); RuleFor(x => x.Limit).InclusiveBetween(1, 100);
    }
}
public sealed record LiveSoloRecordingResponse(Guid Id, Guid MediaSessionId, Guid? RoundId, Guid UserId, string UserName,
    Guid? TeamId, string? TeamName,
    [property: JsonConverter(typeof(StrictPascalCaseEnumConverter<LiveSoloRecordingState>))] LiveSoloRecordingState State,
    Guid ConcurrencyStamp, DateTimeOffset CreatedAt, DateTimeOffset? StartedAt, DateTimeOffset? EndedAt,
    DateTimeOffset KeepUntil, bool DisputeHold, bool Published, long? ByteLength, string FileUrl,
    [property: JsonConverter(typeof(StrictPascalCaseEnumConverter<LiveSoloRecordingFailure>))] LiveSoloRecordingFailure? Failure = null,
    int Chunk = 0)
{
    internal static LiveSoloRecordingResponse From(LiveSoloRecordingView x, Guid competitionId, Guid matchId) => new(x.Id, x.MediaSessionId, x.RoundId, x.UserId,
        x.UserName, x.TeamId, x.TeamName, x.State, x.ConcurrencyStamp, x.CreatedAt, x.StartedAt, x.EndedAt, x.KeepUntil,
        x.DisputeHold, x.Published, x.ByteLength,
        $"/api/v1/competitions/{competitionId}/live-solo/matches/{matchId}/recordings/{x.Id}/file", x.Failure, x.Chunk);
}
public sealed record LiveSoloRecordingsResponse(IReadOnlyList<LiveSoloRecordingResponse> Items, int Total, bool CanJudge, bool CanPublish);
public sealed class ListLiveSoloRecordingsEndpoint(ManageLiveSoloRecordings recordings, IUserContext user)
    : Endpoint<ListLiveSoloRecordingsRequest, Results<Ok<LiveSoloRecordingsResponse>, NotFound>>
{
    public override void Configure()
    {
        Get("/competitions/{competitionId}/live-solo/matches/{matchId}/recordings"); AllowAnonymous();
        Description(x => x.WithName("ListLiveSoloRecordings"));
        Summary(x => x.Summary = "Lists authorized staff recordings or explicitly published post-event replays; never exposes storage keys or media grants.");
    }
    public override async Task<Results<Ok<LiveSoloRecordingsResponse>, NotFound>> ExecuteAsync(ListLiveSoloRecordingsRequest req, CancellationToken ct)
    {
        HttpContext.Response.Headers.CacheControl = "private, no-store";
        var result = await recordings.ListAsync(req.CompetitionId, req.MatchId, user.UserId, req.Staff, req.Offset, req.Limit, ct);
        return result is null ? TypedResults.NotFound() : TypedResults.Ok(new LiveSoloRecordingsResponse(
            result.Items.Select(x => LiveSoloRecordingResponse.From(x, req.CompetitionId, req.MatchId)).ToArray(), result.Total, result.CanJudge, result.CanPublish));
    }
}
