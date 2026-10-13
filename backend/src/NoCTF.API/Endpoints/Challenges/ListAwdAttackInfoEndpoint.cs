using FastEndpoints;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Pagination;
using NoCTF.API.Security;
using NoCTF.Application.Competitions.Awd;

namespace NoCTF.API.Endpoints.Challenges;

public sealed class ListAwdAttackInfoRequest : PaginationRequest
{
    public Guid CompetitionId { get; set; }
    public Guid CompetitionChallengeId { get; set; }
    public Guid TeamId { get; set; }
}

public sealed class ListAwdAttackInfoValidator : Validator<ListAwdAttackInfoRequest>
{
    public ListAwdAttackInfoValidator() => PaginationRules.Add(this);
}

public sealed record AwdAttackInfoResponse(Guid FlagId, int GenerationRound, Guid StoreId, string StoreName,
    int Ordinal, string PublicLocator, DateTimeOffset IssuedAt, int? ExclusiveExpiryRound, double? RemainingActiveSeconds);

public sealed class ListAwdAttackInfoEndpoint(ListAwdAttackInfo list, IUserContext user, TimeProvider time)
    : Endpoint<ListAwdAttackInfoRequest, Results<Ok<ArrayResult<AwdAttackInfoResponse>>, NotFound>>
{
    public override void Configure()
    {
        Get("/competitions/{competitionId}/challenges/{competitionChallengeId}/targets/{teamId}/attack-info");
        AuthSchemes("Bearer");
        Description(builder => builder.WithName("ListAwdAttackInfo"));
        Summary(summary => summary.Summary = "Lists currently valid public AWD locators for a participant target.");
    }

    public override async Task<Results<Ok<ArrayResult<AwdAttackInfoResponse>>, NotFound>> ExecuteAsync(
        ListAwdAttackInfoRequest request, CancellationToken ct)
    {
        HttpContext.Response.Headers.CacheControl = "private,no-store";
        var page = await list.ExecuteAsync(user.UserId, request.CompetitionId, request.CompetitionChallengeId,
            request.TeamId, time.GetUtcNow(), request.Offset, request.Limit, ct);
        return page is null ? TypedResults.NotFound() : TypedResults.Ok(new ArrayResult<AwdAttackInfoResponse>(
            page.Items.Select(item => new AwdAttackInfoResponse(item.FlagId, item.GenerationRound, item.StoreId,
                item.StoreName, item.Ordinal, item.PublicLocator, item.IssuedAt, item.ExclusiveExpiryRound,
                item.RemainingActiveSeconds)).ToArray(), page.Total));
    }
}
