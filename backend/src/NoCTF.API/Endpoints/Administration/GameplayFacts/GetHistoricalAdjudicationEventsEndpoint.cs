using FastEndpoints;
using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Endpoints.Competitions.Events;
using NoCTF.API.Endpoints.GameplayFacts;
using NoCTF.API.Pagination;
using NoCTF.API.Security;
using NoCTF.Application.GameplayFacts.AdjudicationPreview;
using NoCTF.Application.Teams.Moderation;

namespace NoCTF.API.Endpoints.Administration.GameplayFacts;

public sealed class HistoricalAdjudicationEventsRequest
{
    [QueryParam] public string? Cursor { get; set; }
    [QueryParam] public int Limit { get; set; } = 50;
}

public sealed class HistoricalAdjudicationEventsValidator : Validator<HistoricalAdjudicationEventsRequest>
{
    public HistoricalAdjudicationEventsValidator() => RuleFor(request => request.Limit).InclusiveBetween(1, 100);
}

public sealed record AdjudicationEventResponse(Guid EventId, DateTimeOffset OccurredAt,
    CompetitionEventKindProtocol Kind, GameplayFactStateProtocol? State, GameplayFactResultProtocol? Result,
    Guid? ActorUserId, Guid? ParentEventId, bool Readable);
public sealed record HistoricalAdjudicationEventsResponse(IReadOnlyList<AdjudicationEventResponse> Events, string? NextCursor);

internal static class AdjudicationEventMapping
{
    public static AdjudicationEventResponse? Map(AdjudicationEventEvidence? item) => item is null ? null : new(
        item.EventId, item.OccurredAt, CompetitionEventProtocolMapper.ToProtocol(item.Kind),
        item.State is { } state ? GameplayFactMapper.ToProtocol(state) : null,
        item.Result is { } result ? GameplayFactMapper.ToProtocol(result) : null,
        item.ActorUserId, item.ParentEventId, item.Readable);
}

public sealed class GetHistoricalAdjudicationEventsEndpoint(ReadHistoricalAdjudicationEvents read,
    SignedKeysetCursor cursors, ICompetitionModerationAuthorizer authorizer, IUserContext user)
    : Endpoint<HistoricalAdjudicationEventsRequest, Results<Ok<HistoricalAdjudicationEventsResponse>, NotFound, ForbidHttpResult, ProblemHttpResult>>
{
    private const string CursorEndpoint = "gameplay-facts.adjudication-events";

    public override void Configure()
    {
        Get("/admin/competitions/{competitionId}/gameplay-facts/{gameplayFactId}/adjudication-events");
        AuthSchemes("Bearer");
        Description(builder => builder.WithName("AdminGetGameplayFactAdjudicationEvents"));
        Summary(summary =>
        {
            summary.Summary = "Reads ordered adjudication evidence for one gameplay fact.";
            summary.Description = "Read-only signed-keyset pages of event identity, state, result and parent links; excludes Flag values and internal tracks from observers.";
        });
    }

    public override async Task<Results<Ok<HistoricalAdjudicationEventsResponse>, NotFound, ForbidHttpResult, ProblemHttpResult>> ExecuteAsync(
        HistoricalAdjudicationEventsRequest request, CancellationToken ct)
    {
        var competitionId = Route<Guid>("competitionId");
        var factId = Route<Guid>("gameplayFactId");
        if (!await authorizer.CanReadHistoricalAuditAsync(user.UserId, competitionId, ct)) return TypedResults.Forbid();
        var internalTeams = user.IsAdministrator || await authorizer.CanReadInternalHistoricalAuditAsync(user.UserId, competitionId, ct);
        var filter = $"{competitionId:N}|{factId:N}|{user.UserId:N}|{internalTeams}";
        if (!cursors.TryDecode(request.Cursor, CursorEndpoint, filter, out var position))
            return ApiProblems.Problem(statusCode: StatusCodes.Status400BadRequest, title: ApiMessages.Get(ApiMessageId.InvalidCursor));
        var page = await read.ExecuteAsync(competitionId, factId, position?.CreatedAt, position?.Id, request.Limit, internalTeams, ct);
        if (page is null) return TypedResults.NotFound();
        var next = page.NextBeforeOccurredAt is { } at && page.NextBeforeId is { } id
            ? cursors.Encode(CursorEndpoint, filter, new(at, id)) : null;
        return TypedResults.Ok(new HistoricalAdjudicationEventsResponse(page.Events.Select(item => AdjudicationEventMapping.Map(item)!).ToArray(), next));
    }
}
