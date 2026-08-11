using NoCTF.Application.Scoring.Leaderboard;
using NoCTF.Domain.Gameplay;

namespace NoCTF.Application.GameplayFacts.AdjudicationPreview;

public enum AdjudicationDifferenceCertainty : short
{
    Deterministic,
    NeedsReview
}

public enum AdjudicationDifferenceKind : short
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

public sealed record AdjudicationDifference(
    AdjudicationDifferenceKind Kind,
    AdjudicationDifferenceCertainty Certainty);

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
    IReadOnlyList<AdjudicationDifference> Differences);

public sealed record HistoricalAdjudicationPreviewPage(
    HistoricalAdjudicationPreviewReadState State,
    IReadOnlyList<HistoricalAdjudicationDifferenceItem> Items,
    DateTimeOffset? NextBeforeOccurredAt,
    Guid? NextBeforeId);

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
    GameplayFactKind GameplayFactKind,
    GameplayFactResult? CurrentResult,
    DateTimeOffset OccurredAt,
    bool HasEarlierCorrect,
    int EarlierCorrectTeamCount,
    bool BloodEligibilityHistoryRequiresReview,
    IReadOnlyList<GameplayFactResult> HistoricalResults,
    IReadOnlyList<LeaderboardBloodRank> RecordedBloodRanks);

public sealed record HistoricalAdjudicationEvidencePage(
    HistoricalAdjudicationPreviewReadState State,
    IReadOnlyList<HistoricalAdjudicationEvidence> Items);

public interface IHistoricalAdjudicationEvidenceStore
{
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
    public async Task<HistoricalAdjudicationPreviewPage> ExecuteAsync(
        Guid competitionId,
        Guid? competitionChallengeId,
        DateTimeOffset? beforeOccurredAt,
        Guid? beforeId,
        int limit,
        CancellationToken cancellationToken = default)
    {
        var scanLimit = Math.Min(checked(limit * 10), 500);
        var evidencePage = await store.ReadAsync(
            competitionId,
            competitionChallengeId,
            beforeOccurredAt,
            beforeId,
            scanLimit,
            cancellationToken);
        if (evidencePage.State != HistoricalAdjudicationPreviewReadState.Available)
            return new(evidencePage.State, [], null, null);
        if (evidencePage.Items.Count == 0)
            return new(HistoricalAdjudicationPreviewReadState.Available, [], null, null);

        var differences = new List<HistoricalAdjudicationDifferenceItem>(limit);
        var processed = 0;
        foreach (var evidence in evidencePage.Items)
        {
            processed++;
            var issues = Analyze(evidence);
            if (issues.Count == 0)
                continue;

            differences.Add(new(
                evidence.GameplayFactId,
                evidence.CompetitionChallengeId,
                evidence.ChallengeTitle,
                evidence.TeamId,
                evidence.TeamName,
                evidence.GameplayFactKind,
                evidence.CurrentResult,
                ShouldBeDuplicate(evidence) ? GameplayFactResult.Duplicate : null,
                ExpectedBloodRank(evidence),
                evidence.RecordedBloodRanks,
                evidence.OccurredAt,
                issues));
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
            hasMore ? last.GameplayFactId : null);
    }

    private static IReadOnlyList<AdjudicationDifference> Analyze(
        HistoricalAdjudicationEvidence evidence)
    {
        var issues = new List<AdjudicationDifference>();
        if (ShouldBeDuplicate(evidence))
        {
            issues.Add(new(
                AdjudicationDifferenceKind.CurrentCorrectShouldBeDuplicate,
                AdjudicationDifferenceCertainty.Deterministic));
        }
        else if (evidence.CurrentResult == GameplayFactResult.Duplicate
            && !evidence.HasEarlierCorrect)
        {
            issues.Add(new(
                AdjudicationDifferenceKind.DuplicateWithoutCurrentPredecessor,
                AdjudicationDifferenceCertainty.NeedsReview));
        }

        if (evidence.HistoricalResults.Distinct().Count() > 1
            || evidence.HistoricalResults.Any(result => result != evidence.CurrentResult))
        {
            issues.Add(new(
                AdjudicationDifferenceKind.HistoricalResultChanged,
                AdjudicationDifferenceCertainty.NeedsReview));
        }
        if (evidence.CurrentResult is not null && evidence.HistoricalResults.Count == 0)
        {
            issues.Add(new(
                AdjudicationDifferenceKind.MissingAdjudicationRecord,
                AdjudicationDifferenceCertainty.NeedsReview));
        }

        if (evidence.BloodEligibilityHistoryRequiresReview)
        {
            issues.Add(new(
                AdjudicationDifferenceKind.TeamEligibilityHistoryRequiresReview,
                AdjudicationDifferenceCertainty.NeedsReview));
        }
        var expectedBloodRank = ExpectedBloodRank(evidence);
        if (evidence.RecordedBloodRanks.GroupBy(rank => rank).Any(group => group.Count() > 1))
        {
            issues.Add(new(
                AdjudicationDifferenceKind.DuplicateBloodAward,
                AdjudicationDifferenceCertainty.Deterministic));
        }
        if (expectedBloodRank is { } expected)
        {
            if (!evidence.RecordedBloodRanks.Contains(expected))
            {
                issues.Add(new(
                    AdjudicationDifferenceKind.MissingBloodAward,
                    AdjudicationDifferenceCertainty.Deterministic));
            }
            if (evidence.RecordedBloodRanks.Any(rank => rank != expected))
            {
                issues.Add(new(
                    AdjudicationDifferenceKind.WrongBloodRank,
                    AdjudicationDifferenceCertainty.Deterministic));
            }
        }
        else if (!evidence.BloodEligibilityHistoryRequiresReview
            && evidence.RecordedBloodRanks.Count > 0)
        {
            issues.Add(new(
                AdjudicationDifferenceKind.UnexpectedBloodAward,
                evidence.CurrentResult == GameplayFactResult.Correct
                    ? AdjudicationDifferenceCertainty.Deterministic
                    : AdjudicationDifferenceCertainty.NeedsReview));
        }
        return issues;
    }

    private static bool ShouldBeDuplicate(HistoricalAdjudicationEvidence evidence) =>
        evidence.CurrentResult == GameplayFactResult.Correct
        && evidence.HasEarlierCorrect
        && evidence.GameplayFactKind == GameplayFactKind.FlagAttempt;

    private static LeaderboardBloodRank? ExpectedBloodRank(
        HistoricalAdjudicationEvidence evidence)
    {
        if (evidence.GameplayFactKind != GameplayFactKind.FlagAttempt
            || evidence.CurrentResult != GameplayFactResult.Correct
            || evidence.HasEarlierCorrect
            || evidence.BloodEligibilityHistoryRequiresReview)
            return null;
        var rank = evidence.EarlierCorrectTeamCount + 1;
        return rank is >= 1 and <= 3 ? (LeaderboardBloodRank)rank : null;
    }
}
