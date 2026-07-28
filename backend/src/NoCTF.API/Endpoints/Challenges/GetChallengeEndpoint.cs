using FastEndpoints;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Security;
using NoCTF.Application.Challenges.Management;
using NoCTF.Application.Competitions.Koh;

namespace NoCTF.API.Endpoints.Challenges;

public sealed record ChallengeResponse(
    Guid Id,
    Guid CompetitionId,
    Guid ChallengeId,
    string Title,
    string? Description,
    string Direction,
    long BaseScore,
    int Order,
    bool IsPublished,
    int Revision,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    string? ControlFlag = null,
    IReadOnlyList<string>? Urls = null);

public sealed record ChallengeListResponse(IReadOnlyList<ChallengeResponse> Items);

internal static class ChallengeMapper
{
    public static ChallengeResponse ToResponse(
        ChallengeView view,
        KohChallengeAccessView? koh = null) =>
        new(
            view.Id,
            view.CompetitionId,
            view.ChallengeId,
            view.Title,
            view.Description,
            view.Direction,
            view.BaseScore,
            view.Order,
            view.IsPublished,
            view.Revision,
            view.CreatedAt,
            view.UpdatedAt,
            koh?.ControlFlag,
            koh?.Urls);

    public static ChallengeListResponse ToListResponse(IReadOnlyList<ChallengeView> views) =>
        new(views.Select(view => ToResponse(view)).ToArray());
}

public sealed class GetChallengeRequest
{
    public Guid CompetitionId { get; set; }
    public Guid CompetitionChallengeId { get; set; }
}

public sealed class GetChallengeEndpoint(
    GetChallenge get,
    IKohChallengeAccessReader kohAccess,
    IUserContext user) : Endpoint<GetChallengeRequest, Results<Ok<ChallengeResponse>, NotFound>>
{
    public override void Configure()
    {
        Get("/competitions/{competitionId}/challenges/{competitionChallengeId}");
        AllowAnonymous();
        Summary(summary => summary.Summary = "Gets a published challenge.");
    }

    public override async Task<Results<Ok<ChallengeResponse>, NotFound>> ExecuteAsync(
        GetChallengeRequest request,
        CancellationToken ct)
    {
        var item = await get.ExecuteAsync(
            Route<Guid>("competitionId"),
            Route<Guid>("competitionChallengeId"),
            includeUnpublished: false,
            ct);

        if (item is null)
            return TypedResults.NotFound();
        var access = await kohAccess.FindAsync(
            item.CompetitionId,
            item.Id,
            user.UserId,
            ct);
        return TypedResults.Ok(ChallengeMapper.ToResponse(item, access));
    }
}
