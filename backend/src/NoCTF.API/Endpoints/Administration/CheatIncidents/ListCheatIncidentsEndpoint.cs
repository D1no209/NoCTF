using System.Globalization;
using FastEndpoints;
using FluentValidation;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Endpoints.GameplayFacts;
using NoCTF.API.Pagination;
using NoCTF.API.Security;
using NoCTF.API.Serialization;
using NoCTF.Application.GameplayFacts.CheatIncidents;
using NoCTF.Application.Teams.Moderation;
using NoCTF.Domain.Gameplay;
using Riok.Mapperly.Abstractions;
using System.Text.Json.Serialization;

namespace NoCTF.API.Endpoints.Administration.CheatIncidents;

[JsonConverter(typeof(StrictPascalCaseEnumConverter<CheatIncidentStatusProtocol>))]
public enum CheatIncidentStatusProtocol
{
    Pending,
    Confirmed,
    Dismissed,
    Superseded,
    Corrected
}

[Mapper]
internal static partial class CheatIncidentProtocolMapper
{
    [MapEnum(EnumMappingStrategy.ByName)]
    public static partial CheatIncidentStatusProtocol ToProtocol(CheatIncidentStatus value);

    [MapEnum(EnumMappingStrategy.ByName)]
    public static partial CheatIncidentStatus ToDomain(CheatIncidentStatusProtocol value);
}

public sealed class ListCheatIncidentsRequest
{
    [QueryParam] public Guid? SourceTeamId { get; set; }
    [QueryParam] public Guid? OwnerTeamId { get; set; }
    [QueryParam] public Guid? UserId { get; set; }
    [QueryParam] public Guid? CompetitionChallengeId { get; set; }
    [QueryParam] public CheatIncidentStatusProtocol? Status { get; set; }
    [QueryParam] public DateTimeOffset From { get; set; }
    [QueryParam] public DateTimeOffset To { get; set; }
    [QueryParam] public string? Cursor { get; set; }
    [QueryParam] public int Limit { get; set; } = 50;
}

public sealed class ListCheatIncidentsValidator : Validator<ListCheatIncidentsRequest>
{
    public ListCheatIncidentsValidator()
    {
        RuleFor(request => request.Limit).InclusiveBetween(1, 200);
        RuleFor(request => request.From).NotEmpty();
        RuleFor(request => request.To).NotEmpty();
        RuleFor(request => request).Must(request =>
                request.From <= request.To
                && request.To - request.From <= TimeSpan.FromDays(31))
            .WithMessage(_ => ApiMessages.Text(ApiMessageId.ListCheatIncidentsValidationIncidentQueryRangeBetween)).WithErrorCode(ApiMessages.Key(ApiMessageId.ListCheatIncidentsValidationIncidentQueryRangeBetween));
    }
}

public sealed record CheatIncidentListItemResponse(
    Guid GameplayFactId,
    Guid SourceTeamId,
    string SourceTeamName,
    Guid? OwnerTeamId,
    string? OwnerTeamName,
    Guid ActorUserId,
    string SubmittedByUserName,
    Guid CompetitionChallengeId,
    string ChallengeTitle,
    GameplayFactKindProtocol GameplayFactKind,
    GameplayFactResultProtocol Result,
    GameplayFactFailureCodeProtocol FailureCode,
    CheatIncidentStatusProtocol Status,
    Guid? ResolvedByUserId,
    string? ResolvedByUserName,
    DateTimeOffset? ResolvedAt,
    string? ResolutionReason,
    DateTimeOffset SubmittedAt,
    DateTimeOffset DetectedAt,
    bool SourceTeamIsBanned);

public sealed record CheatIncidentListResponse(
    IReadOnlyList<CheatIncidentListItemResponse> Items,
    int PendingCount,
    bool CanDismiss,
    bool CanConfirm,
    string? NextCursor);

public sealed class ListCheatIncidentsEndpoint(
    ListCheatIncidents list,
    SignedKeysetCursor cursors,
    ICompetitionModerationAuthorizer authorizer,
    IUserContext user)
    : Endpoint<ListCheatIncidentsRequest,
        Results<Ok<CheatIncidentListResponse>, NotFound, ForbidHttpResult, ProblemHttpResult>>
{
    private const string CursorEndpoint = "admin.competition.cheat-incidents.list";

    public override void Configure()
    {
        Get("/admin/competitions/{competitionId}/cheat-incidents");
        AuthSchemes("Bearer");
        Description(builder => builder.WithName("AdminListCheatIncidents"));
        Summary(summary =>
        {
            summary.Summary = "Lists Flag ownership and acquisition-evidence incidents.";
            summary.Description =
                "Observer and above may list redacted evidence. Only current scoring facts and immutable adjudication events are used.";
        });
    }

    public override async Task<
        Results<Ok<CheatIncidentListResponse>, NotFound, ForbidHttpResult, ProblemHttpResult>>
        ExecuteAsync(
            ListCheatIncidentsRequest request,
            CancellationToken cancellationToken)
    {
        var competitionId = Route<Guid>("competitionId");
        if (!await authorizer.CanObserveAsync(user.UserId, competitionId, cancellationToken))
            return TypedResults.Forbid();
        var filterKey = FilterKey(competitionId, request);
        if (!cursors.TryDecode(request.Cursor, CursorEndpoint, filterKey, out var position))
        {
            return ApiProblems.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: ApiMessages.Get(ApiMessageId.InvalidCursor));
        }
        var result = await list.ExecuteAsync(new(
            competitionId,
            request.SourceTeamId,
            request.OwnerTeamId,
            request.UserId,
            request.CompetitionChallengeId,
            request.Status is null ? null : CheatIncidentProtocolMapper.ToDomain(request.Status.Value),
            request.From,
            request.To,
            position?.CreatedAt,
            position?.Id,
            request.Limit), cancellationToken);
        if (result is null)
            return TypedResults.NotFound();

        var canDismiss = await authorizer.CanJudgeAsync(
            user.UserId,
            competitionId,
            cancellationToken);
        var canConfirm = await authorizer.CanJudgeAsync(
            user.UserId,
            competitionId,
            cancellationToken);
        var nextCursor = result.Items.Count == request.Limit
            ? cursors.Encode(
                CursorEndpoint,
                filterKey,
                new(result.Items[^1].DetectedAt, result.Items[^1].GameplayFactId))
            : null;
        return TypedResults.Ok(new CheatIncidentListResponse(
            result.Items.Select(Map).ToArray(),
            result.PendingCount,
            canDismiss,
            canConfirm,
            nextCursor));
    }

    private static string FilterKey(
        Guid competitionId,
        ListCheatIncidentsRequest request) =>
        string.Join(
            '|',
            competitionId,
            request.SourceTeamId,
            request.OwnerTeamId,
            request.UserId,
            request.CompetitionChallengeId,
            request.Status,
            request.From.ToString("O", CultureInfo.InvariantCulture),
            request.To.ToString("O", CultureInfo.InvariantCulture));

    private static CheatIncidentListItemResponse Map(CheatIncidentListItem item) => new(
        item.GameplayFactId,
        item.SourceTeamId,
        item.SourceTeamName,
        item.OwnerTeamId,
        item.OwnerTeamName,
        item.ActorUserId,
        item.SubmittedByUserName,
        item.CompetitionChallengeId,
        item.ChallengeTitle,
        GameplayFactMapper.ToProtocol(item.GameplayFactKind),
        GameplayFactMapper.ToProtocol(item.Result),
        GameplayFactMapper.ToProtocol(item.FailureCode),
        CheatIncidentProtocolMapper.ToProtocol(item.Status),
        item.ResolvedByUserId,
        item.ResolvedByUserName,
        item.ResolvedAt,
        item.ResolutionReason,
        item.SubmittedAt,
        item.DetectedAt,
        item.SourceTeamIsBanned);
}
