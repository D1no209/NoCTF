using FastEndpoints;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Security;
using NoCTF.Application.LiveSolo.Matches;
using NoCTF.Application.LiveSolo.Rounds;

namespace NoCTF.API.Endpoints.LiveSolo;

public sealed class DeleteLiveSoloDirectionPresetRequest { public Guid CompetitionId { get; set; } public Guid PresetId { get; set; } }
public sealed class DeleteLiveSoloDirectionPresetEndpoint(ILiveSoloDirectionStore store, IUserContext user)
    : Endpoint<DeleteLiveSoloDirectionPresetRequest, Results<NoContent, NotFound, ForbidHttpResult, Conflict<LiveSoloFailureResponse>>>
{
    public override void Configure()
    {
        Delete("/competitions/{competitionId}/live-solo/direction-presets/{presetId}"); AuthSchemes("Bearer");
        Description(x => x.WithName("DeleteLiveSoloDirectionPreset"));
    }
    public override async Task<Results<NoContent, NotFound, ForbidHttpResult, Conflict<LiveSoloFailureResponse>>> ExecuteAsync(DeleteLiveSoloDirectionPresetRequest req, CancellationToken ct) =>
        await store.DeletePresetAsync(req.CompetitionId, req.PresetId, user.UserId, ct) switch
        {
            null => TypedResults.NoContent(), LiveSoloFailure.NotFound => TypedResults.NotFound(), LiveSoloFailure.Forbidden => TypedResults.Forbid(),
            var failure => TypedResults.Conflict(new LiveSoloFailureResponse(failure.Value))
        };
}
