using NoCTF.Application.Scoring.Leaderboard;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Gameplay;
using NoCTF.Domain.Competitions.Events;

namespace NoCTF.Application.GameplayFacts.AdjudicationPreview;

public enum AdjudicationDifferenceCertainty : short
{
    Deterministic,
    NeedsReview
}

public enum AdjudicationDifferenceKind : short
{
    CurrentCorrectShouldBeDuplicate,
    CurrentDuplicateShouldBeCorrect,
    DuplicateWithoutCurrentPredecessor,
    HistoricalResultChanged,
    MissingAdjudicationRecord,
    TeamEligibilityHistoryRequiresReview,
    MissingBloodAward,
    UnexpectedBloodAward,
    WrongBloodRank,
    DuplicateBloodAward
}

public sealed record AdjudicationDifference(
    AdjudicationDifferenceKind Kind,
    AdjudicationDifferenceCertainty Certainty,
    AdjudicationFindingSeverity Severity = AdjudicationFindingSeverity.Warning,
    AdjudicationFindingClassification Classification = AdjudicationFindingClassification.InsufficientEvidence);

public enum AdjudicationFindingSeverity : short { Information, Warning, Error }
public enum AdjudicationFindingClassification : short
{
    CurrentResultMismatch, IntegrityGap, SuspectedDuplicate, LegalHistoryChange,
    EligibilityAdjustment, InsufficientEvidence, RetainedResult
}
public enum AdjudicationEvidenceCompleteness : short { Complete, Truncated, MissingFields, Ambiguous }

public sealed record AdjudicationEventEvidence(Guid EventId, DateTimeOffset OccurredAt,
    CompetitionEventKind Kind, GameplayFactState? State, GameplayFactResult? Result,
    Guid? ActorUserId = null, Guid? ParentEventId = null, bool Readable = true,
    Guid? GameplayFactId = null);

public sealed record HistoricalAdjudicationDifferenceItem(
    Guid GameplayFactId,
    Guid CompetitionChallengeId,
    string ChallengeTitle,
    Guid? TeamId,
    string? TeamName,
    GameplayFactKind GameplayFactKind,
    GameplayFactResult? CurrentResult,
    GameplayFactResult? DeterministicExpectedResult,
    LeaderboardBloodRank? DeterministicExpectedBloodRank,
    IReadOnlyList<LeaderboardBloodRank> RecordedBloodRanks,
    DateTimeOffset OccurredAt,
    IReadOnlyList<AdjudicationDifference> Differences)
{
    public GameplayFactState CurrentState { get; init; }
    public LeaderboardBloodRank? CurrentProjectedBloodRank { get; init; }
    public AdjudicationEvidenceCompleteness EvidenceCompleteness { get; init; }
    public AdjudicationEventEvidence? LatestProcessingEvent { get; init; }
    public AdjudicationEventEvidence? LatestEffectiveAdjudication { get; init; }
    public int ResultChangeCount { get; init; }
    public int EvidenceCount { get; init; }
    public IReadOnlyList<AdjudicationEventEvidence> EligibilityEvents { get; init; } = [];
}

public sealed record HistoricalAdjudicationPreviewPage(
    HistoricalAdjudicationPreviewReadState State,
    IReadOnlyList<HistoricalAdjudicationDifferenceItem> Items,
    DateTimeOffset? NextBeforeOccurredAt,
    Guid? NextBeforeId)
{
    public int ScannedFacts { get; init; }
}

public enum HistoricalAdjudicationPreviewReadState : short
{
    Available,
    CompetitionNotFound
}

public sealed record HistoricalAdjudicationEvidence(
    Guid GameplayFactId,
    Guid CompetitionChallengeId,
    string ChallengeTitle,
    Guid? TeamId,
    string? TeamName,
    GameMode GameMode,
    GameplayFactKind GameplayFactKind,
    GameplayFactResult? CurrentResult,
    GameplayFactFailureCode? CurrentFailureCode,
    DateTimeOffset OccurredAt,
    bool HasEarlierCorrect,
    int EarlierCorrectTeamCount,
    bool BloodEligibilityHistoryRequiresReview,
    IReadOnlyList<AdjudicationEventEvidence> Events,
    IReadOnlyList<LeaderboardBloodRank> RecordedBloodRanks,
    GameplayFactState CurrentState = GameplayFactState.Completed,
    AdjudicationEvidenceCompleteness Completeness = AdjudicationEvidenceCompleteness.Complete,
    bool HasEligibilityChanges = false,
    bool CurrentBloodEligible = true,
    bool MatchesCurrentInteraction = true,
    IReadOnlyList<AdjudicationEventEvidence>? EligibilityEvents = null);

public sealed record HistoricalAdjudicationEvidencePage(
    HistoricalAdjudicationPreviewReadState State,
    IReadOnlyList<HistoricalAdjudicationEvidence> Items);

public interface IHistoricalAdjudicationEvidenceStore
{
    Task<HistoricalAdjudicationEvidencePage> ReadRestrictedAsync(Guid competitionId, Guid? challengeId,
        DateTimeOffset? beforeOccurredAt, Guid? beforeId, int scanLimit, CancellationToken ct);
    Task<HistoricalAdjudicationEvidencePage> ReadAsync(
        Guid competitionId,
        Guid? competitionChallengeId,
        DateTimeOffset? beforeOccurredAt,
        Guid? beforeId,
        int scanLimit,
        CancellationToken cancellationToken);
}

public sealed class PreviewHistoricalAdjudicationDifferences(
    IHistoricalAdjudicationEvidenceStore store)
{
    public Task<HistoricalAdjudicationPreviewPage> ExecuteAsync(
        Guid competitionId,
        Guid? competitionChallengeId,
        DateTimeOffset? beforeOccurredAt,
        Guid? beforeId,
        int limit,
        CancellationToken cancellationToken = default) =>
        ExecuteAsync(competitionId, competitionChallengeId, beforeOccurredAt, beforeId, limit, false, cancellationToken);

    public async Task<HistoricalAdjudicationPreviewPage> ExecuteAsync(
        Guid competitionId, Guid? competitionChallengeId, DateTimeOffset? beforeOccurredAt,
        Guid? beforeId, int limit, bool includeInformational, CancellationToken cancellationToken = default) =>
        await ExecuteAsync(competitionId, competitionChallengeId, beforeOccurredAt, beforeId, limit,
            includeInformational, false, cancellationToken);

    public async Task<HistoricalAdjudicationPreviewPage> ExecuteAsync(
        Guid competitionId, Guid? competitionChallengeId, DateTimeOffset? beforeOccurredAt,
        Guid? beforeId, int limit, bool includeInformational, bool includeInternalTeams, CancellationToken cancellationToken = default)
    {
        var scanLimit = Math.Min(checked(limit * 10), 500);
        var evidencePage = includeInternalTeams ? await store.ReadAsync(
            competitionId,
            competitionChallengeId,
            beforeOccurredAt,
            beforeId,
            scanLimit,
            cancellationToken) : await store.ReadRestrictedAsync(competitionId, competitionChallengeId, beforeOccurredAt, beforeId, scanLimit, cancellationToken);
        if (evidencePage.State != HistoricalAdjudicationPreviewReadState.Available)
            return new(evidencePage.State, [], null, null);
        if (evidencePage.Items.Count == 0)
            return new(HistoricalAdjudicationPreviewReadState.Available, [], null, null);

        var differences = new List<HistoricalAdjudicationDifferenceItem>(limit);
        var processed = 0;
        foreach (var evidence in evidencePage.Items)
        {
            processed++;
            var item = HistoricalAdjudicationAnalyzer.Analyze(evidence);
            if (item.Differences.Count == 0 || !includeInformational
                && item.Differences.All(issue => issue.Severity == AdjudicationFindingSeverity.Information))
                continue;

            differences.Add(item);
            if (differences.Count == limit)
                break;
        }

        var hasMore = processed < evidencePage.Items.Count
            || evidencePage.Items.Count == scanLimit;
        var last = evidencePage.Items[processed - 1];
        return new(
            HistoricalAdjudicationPreviewReadState.Available,
            differences,
            hasMore ? last.OccurredAt : null,
            hasMore ? last.GameplayFactId : null) { ScannedFacts = processed };
    }

}
