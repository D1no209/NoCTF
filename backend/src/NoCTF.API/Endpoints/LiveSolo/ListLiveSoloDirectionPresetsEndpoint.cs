using FastEndpoints;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Security;
using NoCTF.Application.LiveSolo.Matches;

namespace NoCTF.API.Endpoints.LiveSolo;

public sealed class ListLiveSoloDirectionPresetsRequest { public Guid CompetitionId { get; set; } }
public sealed record LiveSoloDirectionPresetResponse(Guid Id, string Name, string Direction, Guid QuestionGroupId, string QuestionGroupName)
{
    internal static LiveSoloDirectionPresetResponse From(LiveSoloDirectionPresetView value) =>
        new(value.Id, value.Name, value.Direction, value.QuestionGroupId, value.QuestionGroupName);
}
public sealed record ListLiveSoloDirectionPresetsResponse(IReadOnlyList<LiveSoloDirectionPresetResponse> Items);
public sealed class ListLiveSoloDirectionPresetsEndpoint(ILiveSoloDirectionStore store, IUserContext user)
    : Endpoint<ListLiveSoloDirectionPresetsRequest, Results<Ok<ListLiveSoloDirectionPresetsResponse>, NotFound>>
{
    public override void Configure()
    {
        Get("/competitions/{competitionId}/live-solo/direction-presets"); AuthSchemes("Bearer");
        Description(x => x.WithName("ListLiveSoloDirectionPresets"));
    }
    public override async Task<Results<Ok<ListLiveSoloDirectionPresetsResponse>, NotFound>> ExecuteAsync(ListLiveSoloDirectionPresetsRequest req, CancellationToken ct)
    {
        HttpContext.Response.Headers.CacheControl = "private, no-store";
        var result = await store.PresetsAsync(req.CompetitionId, user.UserId, ct);
        return result is null ? TypedResults.NotFound() : TypedResults.Ok(new ListLiveSoloDirectionPresetsResponse(result.Select(LiveSoloDirectionPresetResponse.From).ToArray()));
    }
}
