using FastEndpoints;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Security;
using NoCTF.Application.Challenges.Hints;
using NoCTF.Application.Teams.Moderation;

namespace NoCTF.API.Endpoints.Administration.Challenges;

public sealed record ChallengeHintResponse(
    Guid Id,
    Guid CompetitionChallengeId,
    string Content,
    long Cost,
    DateTimeOffset? PublishedAt,
    DateTimeOffset? DeletedAt,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record ChallengeHintListResponse(IReadOnlyList<ChallengeHintResponse> Items);

internal static class ChallengeHintMapping
{
    public static ChallengeHintResponse ToResponse(ChallengeHintView view) =>
        new(
            view.Id, view.CompetitionChallengeId, view.Content, view.Cost,
            view.PublishedAt, view.DeletedAt, view.CreatedAt, view.UpdatedAt);
}

public sealed class ListChallengeHintsRequest
{
    public Guid CompetitionId { get; set; }
    public Guid CompetitionChallengeId { get; set; }
    [QueryParam]
    public bool IncludeDeleted { get; set; }
}

public sealed class ListChallengeHintsEndpoint(
    ManageChallengeHints hints,
    ICompetitionModerationAuthorizer authorizer,
    IUserContext user)
    : Endpoint<ListChallengeHintsRequest,
        Results<Ok<ChallengeHintListResponse>, NotFound, ForbidHttpResult>>
{
    public override void Configure()
    {
        Get("/admin/competitions/{competitionId}/challenges/{competitionChallengeId}/hints");
        AuthSchemes("Bearer");
        Description(builder => builder.WithName("AdminListCompetitionChallengeHints"));
        Summary(summary =>
        {
            summary.Summary = "Lists competition challenge hints.";
            summary.Description = "Returns every hint and its publication metadata for one challenge instance.";
        });
    }

    public override async Task<Results<Ok<ChallengeHintListResponse>, NotFound, ForbidHttpResult>> ExecuteAsync(
        ListChallengeHintsRequest request,
        CancellationToken ct)
    {
        var competitionId = Route<Guid>("competitionId");
        if (!await authorizer.CanObserveAsync(user.UserId, competitionId, ct))
            return TypedResults.Forbid();
        var items = await hints.ListAsync(
            competitionId,
            request.CompetitionChallengeId,
            request.IncludeDeleted,
            ct);
        return items is null
            ? TypedResults.NotFound()
            : TypedResults.Ok(new ChallengeHintListResponse(
                items.Select(ChallengeHintMapping.ToResponse).ToArray()));
    }
}
