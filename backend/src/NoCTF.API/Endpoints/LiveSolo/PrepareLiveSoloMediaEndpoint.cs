using FastEndpoints;
using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Security;
using NoCTF.Application.LiveSolo.Media;
using NoCTF.Application.LiveSolo.Rounds;

namespace NoCTF.API.Endpoints.LiveSolo;

public sealed class PrepareLiveSoloMediaRequest
{
    public Guid CompetitionId { get; set; }
    public Guid MatchId { get; set; }
    public Guid ExpectedMatchStamp { get; set; }
}
public sealed class PrepareLiveSoloMediaValidator : Validator<PrepareLiveSoloMediaRequest>
{
    public PrepareLiveSoloMediaValidator()
    {
        RuleFor(x => x.CompetitionId).NotEmpty(); RuleFor(x => x.MatchId).NotEmpty(); RuleFor(x => x.ExpectedMatchStamp).NotEmpty();
    }
}
public sealed class PrepareLiveSoloMediaEndpoint(ManageLiveSoloMedia media, IUserContext user)
    : Endpoint<PrepareLiveSoloMediaRequest, Results<Ok<LiveSoloMediaResponse>, ForbidHttpResult, Conflict<LiveSoloFailureResponse>>>
{
    public override void Configure()
    {
        Post("/competitions/{competitionId}/live-solo/matches/{matchId}/media"); AuthSchemes("Bearer");
        Description(x => x.WithName("PrepareLiveSoloMedia")); Summary(x => x.Summary = "Prepares an opaque room after roster lock without granting business access.");
    }
    public override async Task<Results<Ok<LiveSoloMediaResponse>, ForbidHttpResult, Conflict<LiveSoloFailureResponse>>> ExecuteAsync(PrepareLiveSoloMediaRequest req, CancellationToken ct)
    {
        HttpContext.Response.Headers.CacheControl = "private, no-store";
        var result = await media.PrepareAsync(new(req.CompetitionId, req.MatchId, user.UserId, req.ExpectedMatchStamp), ct);
        return result.Session is not null && result.Failure is null ? TypedResults.Ok(LiveSoloMediaResponse.From(result.Session))
            : result.Failure == LiveSoloMediaFailure.Unauthorized ? TypedResults.Forbid()
            : TypedResults.Conflict(new LiveSoloFailureResponse(result.Failure == LiveSoloMediaFailure.InvalidGeneration ? LiveSoloFailure.Conflict : LiveSoloFailure.MediaUnavailable));
    }
}
