using System.Globalization;
using FastEndpoints;
using FluentValidation;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Endpoints.GameplayFacts;
using NoCTF.API.Pagination;
using NoCTF.API.Security;
using NoCTF.Application.GameplayFacts.Management;
using NoCTF.Application.Teams.Moderation;
using NoCTF.Domain.Gameplay;

namespace NoCTF.API.Endpoints.Administration.GameplayFacts;

public sealed class ListAdminGameplayFactsRequest
{
    [QueryParam] public Guid? CompetitionChallengeId { get; set; }
    [QueryParam] public Guid? TeamId { get; set; }
    [QueryParam] public Guid? VictimTeamId { get; set; }
    [QueryParam] public Guid? ActorUserId { get; set; }
    [QueryParam] public GameplayFactKindProtocol? GameplayFactKind { get; set; }
    [QueryParam] public GameplayFactStateProtocol? State { get; set; }
    [QueryParam] public GameplayFactResultProtocol? GameplayFactResult { get; set; }
    [QueryParam] public GameplayFactFailureCodeProtocol? FailureCode { get; set; }
    [QueryParam] public DateTimeOffset? OccurredFrom { get; set; }
    [QueryParam] public DateTimeOffset? OccurredTo { get; set; }
    [QueryParam] public string? Value { get; set; }
    [QueryParam] public GameplayFactReferenceKind? ReferenceKind { get; set; }
    [QueryParam] public Guid? ReferenceId { get; set; }
    [QueryParam] public string? Cursor { get; set; }
    [QueryParam] public int Limit { get; set; } = 50;
}

public sealed class ListAdminGameplayFactsValidator : Validator<ListAdminGameplayFactsRequest>
{
    public ListAdminGameplayFactsValidator() =>
        RuleFor(request => request.Limit).InclusiveBetween(1, 200);
}

public sealed class ListAdminGameplayFactsEndpoint(
    ListGameplayFacts list,
    SignedKeysetCursor cursors,
    ICompetitionModerationAuthorizer authorizer,
    IUserContext user)
    : Endpoint<ListAdminGameplayFactsRequest,
        Results<Ok<GameplayFactListResponse>, ForbidHttpResult, ProblemHttpResult>>
{
    private const string CursorEndpoint = "gameplay-facts.admin.list";

    public override void Configure()
    {
        Get("/admin/competitions/{competitionId}/gameplay-facts");
        AuthSchemes("Bearer");
        Description(builder => builder.WithName("AdminListGameplayFacts"));
        Summary(summary =>
        {
            summary.Summary = "Lists filtered competition gameplay facts.";
            summary.Description = "Returns keyset-paged protected gameplay facts to authorized competition observers.";
        });
    }

    public override async Task<Results<Ok<GameplayFactListResponse>, ForbidHttpResult, ProblemHttpResult>> ExecuteAsync(
        ListAdminGameplayFactsRequest request,
        CancellationToken ct)
    {
        var competitionId = Route<Guid>("competitionId");
        if (!await authorizer.CanObserveAsync(user.UserId, competitionId, ct))
            return TypedResults.Forbid();
        var filterKey = FilterKey(request);
        if (!cursors.TryDecode(request.Cursor, CursorEndpoint, filterKey, out var position))
            return TypedResults.Problem(statusCode: StatusCodes.Status400BadRequest, title: "Invalid cursor.");
        var filter = new GameplayFactListFilter(
            competitionId, request.CompetitionChallengeId, request.TeamId, request.VictimTeamId,
            request.ActorUserId,
            request.GameplayFactKind is null ? null : GameplayFactMapper.ToDomain(request.GameplayFactKind.Value),
            request.State is null ? null : GameplayFactMapper.ToDomain(request.State.Value),
            request.GameplayFactResult is null ? null : GameplayFactMapper.ToDomain(request.GameplayFactResult.Value),
            request.FailureCode is null ? null : GameplayFactMapper.ToDomain(request.FailureCode.Value),
            request.OccurredFrom, request.OccurredTo,
            request.Value, request.ReferenceKind, request.ReferenceId);
        var items = await list.AdminAsync(
            filter, position?.CreatedAt, position?.Id, request.Limit, ct);
        var next = items.Count == request.Limit
            ? cursors.Encode(CursorEndpoint, filterKey, new(items[^1].OccurredAt, items[^1].Id))
            : null;
        return TypedResults.Ok(new GameplayFactListResponse(
            items.Select(GameplayFactListMapping.ToAdminResponse).ToArray(),
            next));
    }

    private static string FilterKey(ListAdminGameplayFactsRequest request) =>
        string.Join('|',
            request.CompetitionChallengeId,
            request.TeamId,
            request.VictimTeamId,
            request.ActorUserId,
            request.GameplayFactKind,
            request.State,
            request.GameplayFactResult,
            request.FailureCode,
            request.OccurredFrom?.ToString("O", CultureInfo.InvariantCulture),
            request.OccurredTo?.ToString("O", CultureInfo.InvariantCulture),
            request.Value,
            request.ReferenceKind,
            request.ReferenceId);
}
