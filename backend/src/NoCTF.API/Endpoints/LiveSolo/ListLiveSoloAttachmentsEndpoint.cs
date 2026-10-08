using System.Text.Json.Serialization;
using FastEndpoints;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Security;
using NoCTF.API.Serialization;
using NoCTF.Application.Challenges.Attachments;
using NoCTF.Application.LiveSolo.Resources;

namespace NoCTF.API.Endpoints.LiveSolo;

public sealed class ListLiveSoloAttachmentsRequest
{
    public Guid CompetitionId { get; set; }
    public Guid MatchId { get; set; }
    public Guid RoundId { get; set; }
    public Guid QuestionId { get; set; }
}
public sealed record LiveSoloAttachmentResponse(Guid Id, string FileName, string ContentType, long ByteLength);
public sealed record LiveSoloAttachmentsResponse(
    [property: JsonConverter(typeof(StrictPascalCaseEnumConverter<AttachmentDeliveryPolicy>))] AttachmentDeliveryPolicy DeliveryPolicy,
    IReadOnlyList<LiveSoloAttachmentResponse> Items);
public sealed class ListLiveSoloAttachmentsEndpoint(AccessLiveSoloAttachments attachments, IUserContext user, TimeProvider clock)
    : Endpoint<ListLiveSoloAttachmentsRequest, Results<Ok<LiveSoloAttachmentsResponse>, NotFound>>
{
    public override void Configure()
    {
        Get("/competitions/{competitionId}/live-solo/matches/{matchId}/rounds/{roundId}/questions/{questionId}/attachments"); AuthSchemes("Bearer");
        Description(x => x.WithName("ListLiveSoloAttachments"));
        Summary(x => x.Summary = "Lists authorized attachment metadata without assigning variants, opening objects or recording evidence.");
    }
    public override async Task<Results<Ok<LiveSoloAttachmentsResponse>, NotFound>> ExecuteAsync(ListLiveSoloAttachmentsRequest req, CancellationToken ct)
    {
        HttpContext.Response.Headers.CacheControl = "private, no-store";
        var result = await attachments.ListAsync(new(req.CompetitionId, req.MatchId, req.RoundId, req.QuestionId, user.UserId, clock.GetUtcNow()), ct);
        return result is null ? TypedResults.NotFound() : TypedResults.Ok(new LiveSoloAttachmentsResponse(result.DeliveryPolicy,
            result.Items.Select(x => new LiveSoloAttachmentResponse(x.Id, x.FileName, x.ContentType, x.ByteLength)).ToArray()));
    }
}
