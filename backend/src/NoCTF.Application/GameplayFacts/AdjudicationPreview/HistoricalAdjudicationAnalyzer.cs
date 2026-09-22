using NoCTF.Application.Scoring.Leaderboard;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Competitions.Events;
using NoCTF.Domain.Gameplay;

namespace NoCTF.Application.GameplayFacts.AdjudicationPreview;

public static class HistoricalAdjudicationAnalyzer
{
    public static HistoricalAdjudicationDifferenceItem Analyze(HistoricalAdjudicationEvidence evidence)
    {
        var events = evidence.Events.Count == 0 ? [] : evidence.Events.OrderBy(item => item.OccurredAt).ThenBy(item => item.EventId).ToArray();
        var adjudications = events.Length == 0 ? [] : events.Where(item => item.Kind == CompetitionEventKind.GameplayFactAdjudicated).ToArray();
        var decisions = adjudications.Length == 0 ? [] : adjudications.Where(item => item.Readable && item.State == GameplayFactState.Completed && item.Result is not null).ToArray();
        var latest = decisions.LastOrDefault();
        var unknown = adjudications.Length == 0 ? [] : adjudications.Where(item => !item.Readable || item.State is null
            || item.State == GameplayFactState.Completed && item.Result is null).ToArray();
        var conflictingTimes = decisions.Length < 2 ? null : decisions.GroupBy(item => item.OccurredAt)
            .Where(group => group.Select(item => item.Result).Distinct().Count() > 1)
            .Select(group => group.Key).ToHashSet();
        var hasConflicts = conflictingTimes is { Count: > 0 };
        var latestTrusted = latest is not null && conflictingTimes?.Contains(latest.OccurredAt) != true
            && !unknown.Any(item => item.OccurredAt >= latest.OccurredAt);
        var changes = decisions.Length < 2 ? 0 : decisions.Zip(decisions.Skip(1)).Count(pair => pair.First.Result != pair.Second.Result);
        var issues = new List<AdjudicationDifference>();
        var incompleteParent = false;
        GameplayFactResult? expectedResult = null;
        void Add(AdjudicationDifferenceKind kind, AdjudicationDifferenceCertainty certainty,
            AdjudicationFindingSeverity severity, AdjudicationFindingClassification classification) =>
            issues.Add(new(kind, certainty, severity, classification));

        if (latestTrusted && evidence.CurrentResult != latest!.Result)
        {
            expectedResult = latest.Result;
            Add(AdjudicationDifferenceKind.HistoricalResultChanged, AdjudicationDifferenceCertainty.Deterministic,
                AdjudicationFindingSeverity.Error, AdjudicationFindingClassification.CurrentResultMismatch);
        }
        else if (decisions.Length > 1 && !hasConflicts && unknown.Length == 0)
            Add(AdjudicationDifferenceKind.HistoricalResultChanged, AdjudicationDifferenceCertainty.Deterministic,
                AdjudicationFindingSeverity.Information, AdjudicationFindingClassification.LegalHistoryChange);

        if (hasConflicts || unknown.Length > 0 || evidence.Completeness == AdjudicationEvidenceCompleteness.Truncated)
            Add(AdjudicationDifferenceKind.HistoricalResultChanged, AdjudicationDifferenceCertainty.NeedsReview,
                AdjudicationFindingSeverity.Warning, AdjudicationFindingClassification.InsufficientEvidence);
        if (evidence.CurrentResult is not null && decisions.Length == 0
            && unknown.Length == 0 && evidence.Completeness != AdjudicationEvidenceCompleteness.Truncated)
            Add(AdjudicationDifferenceKind.MissingAdjudicationRecord, AdjudicationDifferenceCertainty.Deterministic,
                AdjudicationFindingSeverity.Error, AdjudicationFindingClassification.IntegrityGap);
        if (evidence.CurrentResult is not null && evidence.CurrentState is GameplayFactState.Queued or GameplayFactState.Processing or GameplayFactState.PlatformFailed)
            Add(AdjudicationDifferenceKind.HistoricalResultChanged, AdjudicationDifferenceCertainty.Deterministic,
                AdjudicationFindingSeverity.Information, AdjudicationFindingClassification.RetainedResult);
        if (evidence.GameMode == GameMode.Ctf && evidence.CurrentResult == GameplayFactResult.Duplicate && !evidence.HasEarlierCorrect)
            Add(AdjudicationDifferenceKind.DuplicateWithoutCurrentPredecessor, AdjudicationDifferenceCertainty.NeedsReview,
                AdjudicationFindingSeverity.Warning, AdjudicationFindingClassification.InsufficientEvidence);

        LeaderboardBloodRank? currentRank = null;
        if (evidence.GameMode == GameMode.Ctf)
        {
            var historicalAdjustment = evidence.HasEligibilityChanges || changes > 0;
            var comparisonSeverity = historicalAdjustment ? AdjudicationFindingSeverity.Information : AdjudicationFindingSeverity.Warning;
            var comparisonClass = evidence.HasEligibilityChanges ? AdjudicationFindingClassification.EligibilityAdjustment
                : changes > 0 ? AdjudicationFindingClassification.LegalHistoryChange : AdjudicationFindingClassification.InsufficientEvidence;
            if (evidence.CurrentBloodEligible && evidence.MatchesCurrentInteraction
                && evidence.CurrentResult == GameplayFactResult.Correct && !evidence.HasEarlierCorrect
                && evidence.EarlierCorrectTeamCount is >= 0 and < 3)
                currentRank = (LeaderboardBloodRank)(evidence.EarlierCorrectTeamCount + 1);
            // Unknown historical qualification matters only to an actual award comparison.
            // An ordinary nonparticipant with no award and no adjustment is not an anomaly.
            if ((evidence.BloodEligibilityHistoryRequiresReview || !evidence.MatchesCurrentInteraction)
                && (currentRank is not null || evidence.RecordedBloodRanks.Count > 0 || evidence.HasEligibilityChanges))
                Add(AdjudicationDifferenceKind.TeamEligibilityHistoryRequiresReview, AdjudicationDifferenceCertainty.NeedsReview,
                    comparisonSeverity, comparisonClass);

            var bloods = events.Length == 0 ? [] : events.Where(item => IsBlood(item.Kind)).ToArray();
            bool ValidParent(AdjudicationEventEvidence parent) => parent.Kind == CompetitionEventKind.GameplayFactAdjudicated
                && parent.GameplayFactId == evidence.GameplayFactId && parent.Readable
                && parent.State == GameplayFactState.Completed && parent.Result == GameplayFactResult.Correct;
            foreach (var blood in bloods)
            {
                if (blood.ParentEventId is null) continue;
                var parent = events.SingleOrDefault(item => item.EventId == blood.ParentEventId);
                if (parent is not null && ValidParent(parent)) continue;
                var fieldsMissing = parent is { Kind: CompetitionEventKind.GameplayFactAdjudicated }
                    && (!parent.Readable || parent.State is null || parent.Result is null || parent.GameplayFactId is null);
                incompleteParent |= fieldsMissing;
                var uncertain = evidence.Completeness == AdjudicationEvidenceCompleteness.Truncated || fieldsMissing;
                // A complete evidence set contains every relevant event of this fact.
                // An absent parent cannot be a valid adjudication belonging to this fact.
                Add(AdjudicationDifferenceKind.UnexpectedBloodAward,
                    uncertain ? AdjudicationDifferenceCertainty.NeedsReview : AdjudicationDifferenceCertainty.Deterministic,
                    uncertain ? AdjudicationFindingSeverity.Warning : AdjudicationFindingSeverity.Error,
                    uncertain ? AdjudicationFindingClassification.InsufficientEvidence : AdjudicationFindingClassification.IntegrityGap);
            }
            var linkedDuplicate = bloods.Length > 1 && bloods.Where(item => item.ParentEventId is { } parent
                    && adjudications.Any(adjudication => adjudication.EventId == parent
                        && ValidParent(adjudication)))
                .GroupBy(item => item.ParentEventId).Any(group => group.Count() > 1);
            if (linkedDuplicate)
                Add(AdjudicationDifferenceKind.DuplicateBloodAward, AdjudicationDifferenceCertainty.Deterministic,
                    AdjudicationFindingSeverity.Error, AdjudicationFindingClassification.SuspectedDuplicate);
            else if (evidence.RecordedBloodRanks.Count > 1 && evidence.RecordedBloodRanks.GroupBy(rank => rank).Any(group => group.Count() > 1)
                && (bloods.Length != evidence.RecordedBloodRanks.Count
                    || bloods.Any(blood => blood.ParentEventId is not Guid parent
                        || !adjudications.Any(item => item.EventId == parent))))
                Add(AdjudicationDifferenceKind.DuplicateBloodAward, AdjudicationDifferenceCertainty.NeedsReview,
                    AdjudicationFindingSeverity.Warning, AdjudicationFindingClassification.InsufficientEvidence);
            // This compares a current projection with immutable history, not two versions of the same truth.
            if (!evidence.BloodEligibilityHistoryRequiresReview && evidence.MatchesCurrentInteraction
                && evidence.Completeness != AdjudicationEvidenceCompleteness.Truncated)
            {
                if (currentRank is not null && evidence.RecordedBloodRanks.Count == 0)
                    Add(AdjudicationDifferenceKind.MissingBloodAward, AdjudicationDifferenceCertainty.NeedsReview,
                        comparisonSeverity, comparisonClass);
                else if (currentRank is { } expected && evidence.RecordedBloodRanks.Any(recorded => recorded != expected))
                    Add(AdjudicationDifferenceKind.WrongBloodRank, AdjudicationDifferenceCertainty.NeedsReview,
                        comparisonSeverity, comparisonClass);
                else if (currentRank is null && evidence.RecordedBloodRanks.Count > 0)
                    Add(AdjudicationDifferenceKind.UnexpectedBloodAward, AdjudicationDifferenceCertainty.NeedsReview,
                        comparisonSeverity, comparisonClass);
            }
        }
        var completeness = evidence.Completeness == AdjudicationEvidenceCompleteness.Truncated ? evidence.Completeness
            : hasConflicts ? AdjudicationEvidenceCompleteness.Ambiguous
            : unknown.Length > 0 || incompleteParent ? AdjudicationEvidenceCompleteness.MissingFields : evidence.Completeness;
        return new(evidence.GameplayFactId, evidence.CompetitionChallengeId, evidence.ChallengeTitle,
            evidence.TeamId, evidence.TeamName, evidence.GameplayFactKind, evidence.CurrentResult, expectedResult,
            null, evidence.RecordedBloodRanks, evidence.OccurredAt, issues.Count < 2 ? issues.ToArray() : issues.Distinct().ToArray())
        {
            CurrentState = evidence.CurrentState, CurrentProjectedBloodRank = currentRank,
            EvidenceCompleteness = completeness, LatestProcessingEvent = events.LastOrDefault(item => item.State is not null),
            LatestEffectiveAdjudication = latestTrusted ? latest : null, ResultChangeCount = changes,
            EvidenceCount = events.Length, EligibilityEvents = evidence.EligibilityEvents ?? []
        };
    }

    public static bool IsBlood(CompetitionEventKind kind) => kind is CompetitionEventKind.FirstBloodAwarded
        or CompetitionEventKind.SecondBloodAwarded or CompetitionEventKind.ThirdBloodAwarded;
}
