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
using Riok.Mapperly.Abstractions;

namespace NoCTF.API.Endpoints.Administration.GameplayFacts;

public sealed class PreviewHistoricalAdjudicationDifferencesRequest
{
    [QueryParam] public Guid? CompetitionChallengeId { get; set; }
    [QueryParam] public string? Cursor { get; set; }
    [QueryParam] public int Limit { get; set; } = 50;
    [QueryParam] public bool? IncludeInformational { get; set; }
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
    AdjudicationDifferenceCertaintyProtocol Certainty,
    AdjudicationFindingSeverityProtocol Severity,
    AdjudicationFindingClassificationProtocol Classification);

[JsonConverter(typeof(StrictPascalCaseEnumConverter<AdjudicationFindingSeverityProtocol>))]
public enum AdjudicationFindingSeverityProtocol { Information, Warning, Error }
[JsonConverter(typeof(StrictPascalCaseEnumConverter<AdjudicationFindingClassificationProtocol>))]
public enum AdjudicationFindingClassificationProtocol
{
    CurrentResultMismatch, IntegrityGap, SuspectedDuplicate, LegalHistoryChange,
    EligibilityAdjustment, InsufficientEvidence, RetainedResult
}
[JsonConverter(typeof(StrictPascalCaseEnumConverter<AdjudicationEvidenceCompletenessProtocol>))]
public enum AdjudicationEvidenceCompletenessProtocol { Complete, Truncated, MissingFields, Ambiguous }

[Mapper]
internal static partial class AdjudicationFindingMapping
{
    [MapEnum(EnumMappingStrategy.ByName)] public static partial AdjudicationFindingSeverityProtocol Map(AdjudicationFindingSeverity value);
    [MapEnum(EnumMappingStrategy.ByName)] public static partial AdjudicationFindingClassificationProtocol Map(AdjudicationFindingClassification value);
    [MapEnum(EnumMappingStrategy.ByName)] public static partial AdjudicationEvidenceCompletenessProtocol Map(AdjudicationEvidenceCompleteness value);
}

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
    IReadOnlyList<HistoricalAdjudicationDifferenceResponse> Differences)
{
    public GameplayFactStateProtocol CurrentState { get; init; }
    public LeaderboardBloodRankProtocol? CurrentProjectedBloodRank { get; init; }
    public AdjudicationEvidenceCompletenessProtocol EvidenceCompleteness { get; init; }
    public AdjudicationEventResponse? LatestProcessingEvent { get; init; }
    public AdjudicationEventResponse? LatestEffectiveAdjudication { get; init; }
    public int ResultChangeCount { get; init; }
    public int EvidenceCount { get; init; }
    public IReadOnlyList<AdjudicationEventResponse> EligibilityEvents { get; init; } = [];
}

public sealed record HistoricalAdjudicationDifferencePageResponse(
    IReadOnlyList<HistoricalAdjudicationDifferenceItemResponse> Items,
    string? NextCursor)
{
    public int ScannedFacts { get; init; }
    public int AnomalyCount { get; init; }
    public int ReviewCount { get; init; }
    public int InformationCount { get; init; }
}

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
            summary.Description = "Returns bounded read-only analysis of current adjudication and blood-award evidence, distinguishing integrity anomalies, legal changes and incomplete evidence. It never applies corrections.";
        });
    }

    public override async Task<Results<Ok<HistoricalAdjudicationDifferencePageResponse>, NotFound, ForbidHttpResult, ProblemHttpResult>> ExecuteAsync(
        PreviewHistoricalAdjudicationDifferencesRequest request,
        CancellationToken ct)
    {
        var competitionId = Route<Guid>("competitionId");
        if (!await authorizer.CanReadHistoricalAuditAsync(user.UserId, competitionId, ct))
            return TypedResults.Forbid();
        var includeInternal = user.IsAdministrator || await authorizer.CanReadInternalHistoricalAuditAsync(user.UserId, competitionId, ct);
        var filterKey = FilterKey(competitionId, user.UserId, request.CompetitionChallengeId, request.IncludeInformational == true, includeInternal);
        if (!cursors.TryDecode(request.Cursor, CursorEndpoint, filterKey, out var position))
            return TypedResults.Problem(statusCode: StatusCodes.Status400BadRequest, title: "Invalid cursor.");

        var page = await preview.ExecuteAsync(
            competitionId,
            request.CompetitionChallengeId,
            position?.CreatedAt,
            position?.Id,
            request.Limit,
            request.IncludeInformational == true,
            includeInternal,
            ct);
        if (page.State == HistoricalAdjudicationPreviewReadState.CompetitionNotFound)
            return TypedResults.NotFound();
        var nextCursor = page.NextBeforeOccurredAt is DateTimeOffset nextAt
            && page.NextBeforeId is Guid nextId
                ? cursors.Encode(CursorEndpoint, filterKey, new(nextAt, nextId))
                : null;
        return TypedResults.Ok(new HistoricalAdjudicationDifferencePageResponse(
            page.Items.Select(ToResponse).ToArray(),
            nextCursor)
        {
            ScannedFacts = page.ScannedFacts,
            AnomalyCount = page.Items.Count(item => item.Differences.Any(difference => difference.Severity == AdjudicationFindingSeverity.Error)),
            ReviewCount = page.Items.Count(item => !item.Differences.Any(difference => difference.Severity == AdjudicationFindingSeverity.Error)
                && item.Differences.Any(difference => difference.Severity == AdjudicationFindingSeverity.Warning)),
            InformationCount = page.Items.Count(item => item.Differences.All(difference => difference.Severity == AdjudicationFindingSeverity.Information))
        });
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
            ToProtocol(difference.Certainty), AdjudicationFindingMapping.Map(difference.Severity),
            AdjudicationFindingMapping.Map(difference.Classification))).ToArray())
        {
            CurrentState = GameplayFactMapper.ToProtocol(item.CurrentState),
            CurrentProjectedBloodRank = item.CurrentProjectedBloodRank is { } currentRank ? ToProtocol(currentRank) : null,
            EvidenceCompleteness = AdjudicationFindingMapping.Map(item.EvidenceCompleteness),
            LatestProcessingEvent = AdjudicationEventMapping.Map(item.LatestProcessingEvent),
            LatestEffectiveAdjudication = AdjudicationEventMapping.Map(item.LatestEffectiveAdjudication),
            ResultChangeCount = item.ResultChangeCount, EvidenceCount = item.EvidenceCount,
            EligibilityEvents = item.EligibilityEvents.Select(value => AdjudicationEventMapping.Map(value)!).ToArray()
        };

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
        Guid? competitionChallengeId, bool information, bool internalTeams) => string.Join('|',
        competitionId.ToString("N"),
        userId.ToString("N"),
        competitionChallengeId?.ToString("N") ?? "-", information, internalTeams);
}
