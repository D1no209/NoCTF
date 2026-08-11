using System.Text.Json.Serialization;
using FastEndpoints;
using FluentValidation;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Endpoints.GameplayFacts;
using NoCTF.API.Pagination;
using NoCTF.API.Serialization;
using NoCTF.API.Security;
using NoCTF.Application.GameplayFacts.AdjudicationPreview;
using NoCTF.Application.Scoring.Leaderboard;
using NoCTF.Application.Teams.Moderation;

namespace NoCTF.API.Endpoints.Administration.GameplayFacts;

public sealed class PreviewHistoricalAdjudicationDifferencesRequest
{
    [QueryParam] public Guid? CompetitionChallengeId { get; set; }
    [QueryParam] public string? Cursor { get; set; }
    [QueryParam] public int Limit { get; set; } = 50;
}

public sealed class PreviewHistoricalAdjudicationDifferencesValidator
    : Validator<PreviewHistoricalAdjudicationDifferencesRequest>
{
    public PreviewHistoricalAdjudicationDifferencesValidator() =>
        RuleFor(request => request.Limit).InclusiveBetween(1, 100);
}

[JsonConverter(typeof(StrictPascalCaseEnumConverter<AdjudicationDifferenceCertaintyProtocol>))]
public enum AdjudicationDifferenceCertaintyProtocol
{
    Deterministic,
    NeedsReview
}

[JsonConverter(typeof(StrictPascalCaseEnumConverter<AdjudicationDifferenceKindProtocol>))]
public enum AdjudicationDifferenceKindProtocol
{
    CurrentCorrectShouldBeDuplicate,
    DuplicateWithoutCurrentPredecessor,
    HistoricalResultChanged,
    MissingAdjudicationRecord,
    TeamEligibilityHistoryRequiresReview,
    MissingBloodAward,
    UnexpectedBloodAward,
    WrongBloodRank,
    DuplicateBloodAward
}

[JsonConverter(typeof(StrictPascalCaseEnumConverter<LeaderboardBloodRankProtocol>))]
public enum LeaderboardBloodRankProtocol
{
    First,
    Second,
    Third
}

public sealed record HistoricalAdjudicationDifferenceResponse(
    AdjudicationDifferenceKindProtocol Kind,
    AdjudicationDifferenceCertaintyProtocol Certainty);

public sealed record HistoricalAdjudicationDifferenceItemResponse(
    Guid GameplayFactId,
    Guid CompetitionChallengeId,
    string ChallengeTitle,
    Guid? TeamId,
    string? TeamName,
    GameplayFactKindProtocol GameplayFactKind,
    GameplayFactResultProtocol? CurrentResult,
    GameplayFactResultProtocol? DeterministicExpectedResult,
    LeaderboardBloodRankProtocol? DeterministicExpectedBloodRank,
    IReadOnlyList<LeaderboardBloodRankProtocol> RecordedBloodRanks,
    DateTimeOffset OccurredAt,
    IReadOnlyList<HistoricalAdjudicationDifferenceResponse> Differences);

public sealed record HistoricalAdjudicationDifferencePageResponse(
    IReadOnlyList<HistoricalAdjudicationDifferenceItemResponse> Items,
    string? NextCursor);

public sealed class PreviewHistoricalAdjudicationDifferencesEndpoint(
    PreviewHistoricalAdjudicationDifferences preview,
    SignedKeysetCursor cursors,
    ICompetitionModerationAuthorizer authorizer,
    IUserContext user)
    : Endpoint<PreviewHistoricalAdjudicationDifferencesRequest,
        Results<Ok<HistoricalAdjudicationDifferencePageResponse>, NotFound, ForbidHttpResult, ProblemHttpResult>>
{
    private const string CursorEndpoint = "gameplay-facts.adjudication-differences.preview";

    public override void Configure()
    {
        Get("/admin/competitions/{competitionId}/gameplay-facts/adjudication-differences");
        AuthSchemes("Bearer");
        Description(builder => builder.WithName("AdminPreviewHistoricalAdjudicationDifferences"));
        Summary(summary =>
        {
            summary.Summary = "Previews historical gameplay adjudication differences.";
            summary.Description = "Returns a bounded, read-only analysis of CTF Flag results and recorded blood awards. It never applies corrections.";
        });
    }

    public override async Task<Results<Ok<HistoricalAdjudicationDifferencePageResponse>, NotFound, ForbidHttpResult, ProblemHttpResult>> ExecuteAsync(
        PreviewHistoricalAdjudicationDifferencesRequest request,
        CancellationToken ct)
    {
        var competitionId = Route<Guid>("competitionId");
        if (!await authorizer.CanObserveAsync(user.UserId, competitionId, ct))
            return TypedResults.Forbid();
        var filterKey = FilterKey(competitionId, user.UserId, request.CompetitionChallengeId);
        if (!cursors.TryDecode(request.Cursor, CursorEndpoint, filterKey, out var position))
            return TypedResults.Problem(statusCode: StatusCodes.Status400BadRequest, title: "Invalid cursor.");

        var page = await preview.ExecuteAsync(
            competitionId,
            request.CompetitionChallengeId,
            position?.CreatedAt,
            position?.Id,
            request.Limit,
            ct);
        if (page.State == HistoricalAdjudicationPreviewReadState.CompetitionNotFound)
            return TypedResults.NotFound();
        var nextCursor = page.NextBeforeOccurredAt is DateTimeOffset nextAt
            && page.NextBeforeId is Guid nextId
                ? cursors.Encode(CursorEndpoint, filterKey, new(nextAt, nextId))
                : null;
        return TypedResults.Ok(new HistoricalAdjudicationDifferencePageResponse(
            page.Items.Select(ToResponse).ToArray(),
            nextCursor));
    }

    private static HistoricalAdjudicationDifferenceItemResponse ToResponse(
        HistoricalAdjudicationDifferenceItem item) => new(
        item.GameplayFactId,
        item.CompetitionChallengeId,
        item.ChallengeTitle,
        item.TeamId,
        item.TeamName,
        GameplayFactMapper.ToProtocol(item.GameplayFactKind),
        item.CurrentResult is { } current ? GameplayFactMapper.ToProtocol(current) : null,
        item.DeterministicExpectedResult is { } expected
            ? GameplayFactMapper.ToProtocol(expected)
            : null,
        item.DeterministicExpectedBloodRank is { } bloodRank ? ToProtocol(bloodRank) : null,
        item.RecordedBloodRanks.Select(ToProtocol).ToArray(),
        item.OccurredAt,
        item.Differences.Select(difference => new HistoricalAdjudicationDifferenceResponse(
            ToProtocol(difference.Kind),
            ToProtocol(difference.Certainty))).ToArray());

    private static AdjudicationDifferenceKindProtocol ToProtocol(AdjudicationDifferenceKind kind) =>
        kind switch
        {
            AdjudicationDifferenceKind.CurrentCorrectShouldBeDuplicate =>
                AdjudicationDifferenceKindProtocol.CurrentCorrectShouldBeDuplicate,
            AdjudicationDifferenceKind.DuplicateWithoutCurrentPredecessor =>
                AdjudicationDifferenceKindProtocol.DuplicateWithoutCurrentPredecessor,
            AdjudicationDifferenceKind.HistoricalResultChanged =>
                AdjudicationDifferenceKindProtocol.HistoricalResultChanged,
            AdjudicationDifferenceKind.MissingAdjudicationRecord =>
                AdjudicationDifferenceKindProtocol.MissingAdjudicationRecord,
            AdjudicationDifferenceKind.TeamEligibilityHistoryRequiresReview =>
                AdjudicationDifferenceKindProtocol.TeamEligibilityHistoryRequiresReview,
            AdjudicationDifferenceKind.MissingBloodAward =>
                AdjudicationDifferenceKindProtocol.MissingBloodAward,
            AdjudicationDifferenceKind.UnexpectedBloodAward =>
                AdjudicationDifferenceKindProtocol.UnexpectedBloodAward,
            AdjudicationDifferenceKind.WrongBloodRank =>
                AdjudicationDifferenceKindProtocol.WrongBloodRank,
            AdjudicationDifferenceKind.DuplicateBloodAward =>
                AdjudicationDifferenceKindProtocol.DuplicateBloodAward,
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null)
        };

    private static AdjudicationDifferenceCertaintyProtocol ToProtocol(
        AdjudicationDifferenceCertainty certainty) => certainty switch
    {
        AdjudicationDifferenceCertainty.Deterministic =>
            AdjudicationDifferenceCertaintyProtocol.Deterministic,
        AdjudicationDifferenceCertainty.NeedsReview =>
            AdjudicationDifferenceCertaintyProtocol.NeedsReview,
        _ => throw new ArgumentOutOfRangeException(nameof(certainty), certainty, null)
    };

    private static LeaderboardBloodRankProtocol ToProtocol(LeaderboardBloodRank rank) => rank switch
    {
        LeaderboardBloodRank.First => LeaderboardBloodRankProtocol.First,
        LeaderboardBloodRank.Second => LeaderboardBloodRankProtocol.Second,
        LeaderboardBloodRank.Third => LeaderboardBloodRankProtocol.Third,
        _ => throw new ArgumentOutOfRangeException(nameof(rank), rank, null)
    };

    private static string FilterKey(
        Guid competitionId,
        Guid userId,
        Guid? competitionChallengeId) => string.Join('|',
        competitionId.ToString("N"),
        userId.ToString("N"),
        competitionChallengeId?.ToString("N") ?? "-");
}
