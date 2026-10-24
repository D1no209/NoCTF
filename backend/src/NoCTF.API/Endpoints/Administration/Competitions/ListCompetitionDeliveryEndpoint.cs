using FastEndpoints;
using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Net.Http.Headers;
using NoCTF.API.Endpoints.Administration.ChallengeBank;
using NoCTF.API.Endpoints.Administration.Runtime;
using NoCTF.API.Pagination;
using NoCTF.API.Security;
using NoCTF.Application.Competitions.Delivery;
using NoCTF.Application.Teams.Moderation;

namespace NoCTF.API.Endpoints.Administration.Competitions;

public sealed class ListCompetitionDeliveryRequest : PaginationRequest
{
    public Guid CompetitionId { get; set; }
    [QueryParam] public Guid? CompetitionChallengeId { get; set; }
    [QueryParam] public Guid? TeamId { get; set; }
    [QueryParam] public bool IncludeHistory { get; set; }
}
public sealed class ListCompetitionDeliveryValidator : Validator<ListCompetitionDeliveryRequest>
{
    public ListCompetitionDeliveryValidator() { PaginationRules.Add(this); RuleFor(request => request.CompetitionId).NotEmpty(); }
}
public sealed record CompetitionDeliveryAttachmentResponse(Guid Id, string FileName, bool Deleted);
public sealed record CompetitionDeliveryFlagResponse(Guid Id, string Flag, ChallengeFlagMatchKindProtocol MatchKind,
    RuntimeFlagStateProtocol State, Guid? AttachmentId, Guid? RuntimeInstanceId,
    DateTimeOffset CreatedAt, DateTimeOffset? ValidStart, DateTimeOffset? ValidUntil);
public sealed record CompetitionDeliveryResponse(Guid TeamId, string TeamName, Guid CompetitionChallengeId,
    string ChallengeTitle, bool RandomAttachment, IReadOnlyList<CompetitionDeliveryAttachmentResponse> Attachments,
    IReadOnlyList<CompetitionDeliveryFlagResponse> Flags);

public sealed class ListCompetitionDeliveryEndpoint(QueryCompetitionDelivery delivery, ICompetitionModerationAuthorizer authorizer,
    IUserContext user, TimeProvider clock)
    : Endpoint<ListCompetitionDeliveryRequest, Results<Ok<ArrayResult<CompetitionDeliveryResponse>>, ForbidHttpResult, NotFound>>
{
    public override void Configure()
    {
        Get("/admin/competitions/{competitionId}/deliveries"); AuthSchemes("Bearer");
        Description(builder => builder.WithName("AdminListCompetitionDeliveries"));
        Summary(summary => { summary.Summary = "Lists team Flag and attachment delivery.";
            summary.Description = "Reads existing assignments without generating Flags or assigning attachments. Responses must not be cached."; });
    }
    public override async Task<Results<Ok<ArrayResult<CompetitionDeliveryResponse>>, ForbidHttpResult, NotFound>> ExecuteAsync(
        ListCompetitionDeliveryRequest request, CancellationToken ct)
    {
        HttpContext.Response.GetTypedHeaders().CacheControl = new CacheControlHeaderValue { NoStore = true, NoCache = true };
        if (!await authorizer.CanObserveAsync(user.UserId, request.CompetitionId, ct)) return TypedResults.Forbid();
        var result = await delivery.ExecuteAsync(new(request.CompetitionId, request.CompetitionChallengeId, request.TeamId,
            request.IncludeHistory, request.Offset, request.Limit, request.Desc, clock.GetUtcNow()), ct);
        return result is null ? TypedResults.NotFound() : TypedResults.Ok(new ArrayResult<CompetitionDeliveryResponse>(
            result.Items.Select(item => new CompetitionDeliveryResponse(item.TeamId, item.TeamName, item.CompetitionChallengeId,
                item.ChallengeTitle, item.RandomAttachment, item.Attachments.Select(file => new CompetitionDeliveryAttachmentResponse(file.Id, file.FileName, file.Deleted)).ToArray(),
                item.Flags.Select(flag => new CompetitionDeliveryFlagResponse(flag.Id, flag.Flag, (ChallengeFlagMatchKindProtocol)flag.MatchKind,
                    (RuntimeFlagStateProtocol)flag.State, flag.AttachmentId, flag.RuntimeInstanceId, flag.CreatedAt, flag.ValidStart, flag.ValidUntil)).ToArray())).ToArray(), result.Total));
    }
}
