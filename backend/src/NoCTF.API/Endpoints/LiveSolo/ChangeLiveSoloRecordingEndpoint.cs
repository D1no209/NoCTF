using System.Text.Json.Serialization;
using FastEndpoints;
using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Security;
using NoCTF.API.Serialization;
using NoCTF.Application.LiveSolo.Media;
using NoCTF.Application.LiveSolo.Rounds;
using NoCTF.Domain.LiveSolo;

namespace NoCTF.API.Endpoints.LiveSolo;

public sealed class ChangeLiveSoloRecordingRequest
{
    public Guid CompetitionId { get; set; }
    public Guid MatchId { get; set; }
    public Guid RecordingId { get; set; }
    public Guid ExpectedStamp { get; set; }
    [JsonConverter(typeof(StrictPascalCaseEnumConverter<LiveSoloRecordingAction>))] public required LiveSoloRecordingAction Action { get; set; }
    public string Reason { get; set; } = "";
}
public sealed class ChangeLiveSoloRecordingValidator : Validator<ChangeLiveSoloRecordingRequest>
{
    public ChangeLiveSoloRecordingValidator()
    {
        RuleFor(x => x.CompetitionId).NotEmpty(); RuleFor(x => x.MatchId).NotEmpty(); RuleFor(x => x.RecordingId).NotEmpty();
        RuleFor(x => x.ExpectedStamp).NotEmpty(); RuleFor(x => x.Action).IsInEnum(); RuleFor(x => x.Reason).NotEmpty().MaximumLength(4000);
    }
}
public sealed class ChangeLiveSoloRecordingEndpoint(ManageLiveSoloRecordings recordings, IUserContext user)
    : Endpoint<ChangeLiveSoloRecordingRequest, Results<Ok<LiveSoloRecordingResponse>, NotFound, ForbidHttpResult,
        Conflict<LiveSoloFailureResponse>, UnprocessableEntity<LiveSoloFailureResponse>>>
{
    public override void Configure()
    {
        Post("/competitions/{competitionId}/live-solo/matches/{matchId}/recordings/{recordingId}/decisions"); AuthSchemes("Bearer");
        Description(x => x.WithName("ChangeLiveSoloRecording"));
        Summary(x => x.Summary = "Records a reasoned and revision-fenced hold or replay publication decision; publishing requires the entire event to have ended.");
    }
    public override async Task<Results<Ok<LiveSoloRecordingResponse>, NotFound, ForbidHttpResult,
        Conflict<LiveSoloFailureResponse>, UnprocessableEntity<LiveSoloFailureResponse>>> ExecuteAsync(ChangeLiveSoloRecordingRequest req, CancellationToken ct)
    {
        var result = await recordings.ChangeAsync(new(req.CompetitionId, req.MatchId, req.RecordingId, user.UserId, req.ExpectedStamp, req.Action, req.Reason), ct);
        return result.Failure switch
        {
            null when result.Recording is not null => TypedResults.Ok(LiveSoloRecordingResponse.From(result.Recording, req.CompetitionId, req.MatchId)),
            LiveSoloFailure.NotFound => TypedResults.NotFound(), LiveSoloFailure.Forbidden => TypedResults.Forbid(),
            LiveSoloFailure.InvalidConfiguration => TypedResults.UnprocessableEntity(new LiveSoloFailureResponse(result.Failure.Value)),
            _ => TypedResults.Conflict(new LiveSoloFailureResponse(result.Failure ?? LiveSoloFailure.Conflict))
        };
    }
}
