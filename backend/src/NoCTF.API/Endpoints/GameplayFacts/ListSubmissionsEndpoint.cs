using FastEndpoints;
using FluentValidation;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Pagination;
using NoCTF.API.Security;
using NoCTF.Application.GameplayFacts.Management;
using NoCTF.Domain.Gameplay;

namespace NoCTF.API.Endpoints.GameplayFacts;

public sealed class ListGameplayFactsRequest
{
    [QueryParam]
    public Guid? CompetitionChallengeId { get; set; }
    [QueryParam]
    public GameplayFactKindProtocol? Kind { get; set; }
    [QueryParam]
    public string? Cursor { get; set; }
    [QueryParam]
    public int Limit { get; set; } = 50;
}

public sealed class ListGameplayFactsValidator : Validator<ListGameplayFactsRequest>
{
    public ListGameplayFactsValidator() =>
        RuleFor(request => request.Limit).InclusiveBetween(1, 200);
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

public sealed record GameplayFactListResponse(
    IReadOnlyList<GameplayFactListItemResponse> Items,
    string? NextCursor);

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
    SignedKeysetCursor cursors,
    IUserContext user)
    : Endpoint<ListGameplayFactsRequest, Results<Ok<GameplayFactListResponse>, NotFound, ProblemHttpResult>>
{
    private const string CursorEndpoint = "gameplay-facts.player.list";

    public override void Configure()
    {
        Get("/competitions/{competitionId}/gameplay-facts");
        AuthSchemes("Bearer");
        Summary(summary => summary.Summary = "Lists the current team's gameplay facts.");
    }

    public override async Task<Results<Ok<GameplayFactListResponse>, NotFound, ProblemHttpResult>> ExecuteAsync(
        ListGameplayFactsRequest request,
        CancellationToken ct)
    {
        var competitionId = Route<Guid>("competitionId");
        var cursorScope = string.Join(':',
            competitionId.ToString("N"),
            user.UserId.ToString("N"),
            request.CompetitionChallengeId?.ToString("N") ?? "all",
            request.Kind?.ToString() ?? "all");
        if (!cursors.TryDecode(request.Cursor, CursorEndpoint, cursorScope, out var position))
            return TypedResults.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "Invalid cursor.");
        var items = await list.PlayerAsync(
            competitionId,
            user.UserId,
            request.CompetitionChallengeId,
            request.Kind is null ? null : GameplayFactMapper.ToDomain(request.Kind.Value),
            position?.CreatedAt,
            position?.Id,
            request.Limit,
            ct);
        if (items is null)
            return TypedResults.NotFound();
        var next = items.Count == request.Limit
            ? cursors.Encode(CursorEndpoint, cursorScope, new(items[^1].OccurredAt, items[^1].Id))
            : null;
        return TypedResults.Ok(new GameplayFactListResponse(
            items.Select(GameplayFactListMapping.ToPlayerResponse).ToArray(),
            next));
    }
}
