using FastEndpoints;
using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Pagination;
using NoCTF.API.Security;
using NoCTF.Application.GameplayFacts.Management;
using NoCTF.Domain.Gameplay;

namespace NoCTF.API.Endpoints.GameplayFacts;

public sealed class ListGameplayFactsRequest : PaginationRequest
{
    [QueryParam]
    public Guid? CompetitionChallengeId { get; set; }
    [QueryParam]
    public GameplayFactKindProtocol? Kind { get; set; }
}

public sealed class ListGameplayFactsValidator : Validator<ListGameplayFactsRequest>
{
    public ListGameplayFactsValidator() =>
        PaginationRules.Add(this);
}

public sealed record GameplayFactListItemResponse(
    Guid Id,
    Guid CompetitionId,
    Guid CompetitionChallengeId,
    Guid? TeamId,
    Guid? VictimTeamId,
    Guid? ActorUserId,
    GameplayFactKindProtocol Kind,
    GameplayFactStateProtocol State,
    GameplayFactResultProtocol? Result,
    GameplayFactFailureCodeProtocol? FailureCode,
    GameplayFactReferenceKind? ReferenceKind,
    Guid? ReferenceId,
    string? Value,
    DateTimeOffset OccurredAt,
    DateTimeOffset UpdatedAt);

public sealed class GameplayFactListResponse : ArrayResult<GameplayFactListItemResponse>
{
    public GameplayFactListResponse() { }

    public GameplayFactListResponse(GameplayFactListItemResponse[] items, int total)
        : base(items, total) { }
}

internal static class GameplayFactListMapping
{
    public static GameplayFactListItemResponse ToAdminResponse(GameplayFactListItem item) =>
        ToResponse(item, item.Value);

    public static GameplayFactListItemResponse ToPlayerResponse(GameplayFactListItem item) =>
        ToResponse(item, null);

    private static GameplayFactListItemResponse ToResponse(GameplayFactListItem item, string? value) =>
        new(
            item.Id, item.CompetitionId, item.CompetitionChallengeId, item.TeamId,
            item.VictimTeamId, item.ActorUserId,
            GameplayFactMapper.ToProtocol(item.Kind),
            GameplayFactMapper.ToProtocol(item.State),
            item.Result is null ? null : GameplayFactMapper.ToProtocol(item.Result.Value),
            item.FailureCode is null ? null : GameplayFactMapper.ToProtocol(item.FailureCode.Value),
            item.ReferenceKind, item.ReferenceId, value, item.OccurredAt, item.UpdatedAt);
}

public sealed class ListGameplayFactsEndpoint(
    ListGameplayFacts list,
    IUserContext user)
    : Endpoint<ListGameplayFactsRequest, Results<Ok<GameplayFactListResponse>, NotFound>>
{
    public override void Configure()
    {
        Get("/competitions/{competitionId}/gameplay-facts");
        AuthSchemes("Bearer");
        Summary(summary => { summary.Summary = "Lists the current team's gameplay facts."; summary.Description = summary.Summary; });
    }

    public override async Task<Results<Ok<GameplayFactListResponse>, NotFound>> ExecuteAsync(
        ListGameplayFactsRequest request,
        CancellationToken ct)
    {
        var competitionId = Route<Guid>("competitionId");
        var page = await list.PlayerPageAsync(
            competitionId,
            user.UserId,
            request.CompetitionChallengeId,
            request.Kind is null ? null : GameplayFactMapper.ToDomain(request.Kind.Value),
            request.Offset,
            request.Limit,
            request.Desc,
            ct);
        if (page is null)
            return TypedResults.NotFound();
        return TypedResults.Ok(new GameplayFactListResponse(
            page.Items.Select(GameplayFactListMapping.ToPlayerResponse).ToArray(),
            page.Total));
    }
}
