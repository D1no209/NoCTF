using NoCTF.Application.GameplayFacts.AdjudicationPreview;
using NoCTF.Application.Scoring.Leaderboard;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Competitions.Events;
using NoCTF.Domain.Gameplay;

namespace NoCTF.Tests.Unit.GameModes;

public sealed class HistoricalAdjudicationAnalyzerTests
{
    private static readonly DateTimeOffset At = new(2026, 9, 18, 0, 0, 0, TimeSpan.Zero);
    private static AdjudicationEventEvidence Decision(int seconds, GameplayFactResult result,
        GameplayFactState? state = GameplayFactState.Completed) =>
        new(Guid.NewGuid(), At.AddSeconds(seconds), CompetitionEventKind.GameplayFactAdjudicated, state, result);

    private static HistoricalAdjudicationEvidence Evidence(params AdjudicationEventEvidence[] events) =>
        new(Guid.NewGuid(), Guid.NewGuid(), "challenge", Guid.NewGuid(), "team", GameMode.Ctf,
            GameplayFactKind.FlagAttempt, GameplayFactResult.Correct, null, At, true, 0, false, events, []);

    [Test]
    public async Task Correct_wrong_correct_is_an_ordered_legal_change_not_a_result_conflict()
    {
        var first = Decision(1, GameplayFactResult.Correct);
        var middle = Decision(2, GameplayFactResult.Wrong);
        var last = Decision(3, GameplayFactResult.Correct);
        var result = HistoricalAdjudicationAnalyzer.Analyze(Evidence(last, first, middle));
        await Assert.That(result.LatestEffectiveAdjudication!.EventId).IsEqualTo(last.EventId);
        await Assert.That(result.ResultChangeCount).IsEqualTo(2);
        await Assert.That(result.Differences.All(item => item.Severity == AdjudicationFindingSeverity.Information)).IsTrue();
        await Assert.That(result.DeterministicExpectedResult).IsNull();
    }

    [Test]
    public async Task Repeated_identical_results_do_not_prove_duplicate_execution()
    {
        var result = HistoricalAdjudicationAnalyzer.Analyze(Evidence(Decision(1, GameplayFactResult.Correct), Decision(2, GameplayFactResult.Correct)));
        await Assert.That(result.ResultChangeCount).IsEqualTo(0);
        await Assert.That(result.Differences.All(item => item.Classification == AdjudicationFindingClassification.LegalHistoryChange)).IsTrue();
    }

    [Test, Arguments(GameplayFactState.Queued), Arguments(GameplayFactState.Processing), Arguments(GameplayFactState.PlatformFailed)]
    public async Task A_rejudge_may_retain_the_previous_business_result(GameplayFactState state)
    {
        var completed = Decision(1, GameplayFactResult.Correct);
        var processing = Decision(2, GameplayFactResult.Correct, state);
        var result = HistoricalAdjudicationAnalyzer.Analyze(Evidence(completed, processing) with { CurrentState = state });
        await Assert.That(result.LatestEffectiveAdjudication!.EventId).IsEqualTo(completed.EventId);
        await Assert.That(result.LatestProcessingEvent!.State).IsEqualTo(state);
        await Assert.That(result.Differences.Any(item => item.Classification == AdjudicationFindingClassification.RetainedResult)).IsTrue();
        await Assert.That(result.Differences.All(item => item.Severity == AdjudicationFindingSeverity.Information)).IsTrue();
    }

    [Test]
    public async Task Only_a_trusted_latest_decision_establishes_current_result_mismatch()
    {
        var result = HistoricalAdjudicationAnalyzer.Analyze(Evidence(Decision(1, GameplayFactResult.Correct)) with { CurrentResult = GameplayFactResult.Wrong });
        await Assert.That(result.DeterministicExpectedResult).IsEqualTo(GameplayFactResult.Correct);
        await Assert.That(result.Differences.Single().Severity).IsEqualTo(AdjudicationFindingSeverity.Error);
    }

    [Test]
    public async Task Conflicting_simultaneous_events_are_not_ordered_causally_by_guid()
    {
        var result = HistoricalAdjudicationAnalyzer.Analyze(Evidence(Decision(1, GameplayFactResult.Correct), Decision(1, GameplayFactResult.Wrong)));
        await Assert.That(result.LatestEffectiveAdjudication).IsNull();
        await Assert.That(result.EvidenceCompleteness).IsEqualTo(AdjudicationEvidenceCompleteness.Ambiguous);
        await Assert.That(result.Differences.All(item => item.Severity != AdjudicationFindingSeverity.Error)).IsTrue();
    }

    [Test, Arguments(false), Arguments(true)]
    public async Task Missing_events_are_not_declared_missing_outside_a_truncated_scan(bool truncated)
    {
        var result = HistoricalAdjudicationAnalyzer.Analyze(Evidence() with
        {
            Completeness = truncated ? AdjudicationEvidenceCompleteness.Truncated : AdjudicationEvidenceCompleteness.Complete
        });
        await Assert.That(result.Differences.Any(item => item.Classification == AdjudicationFindingClassification.IntegrityGap)).IsEqualTo(!truncated);
        await Assert.That(result.Differences.Any(item => item.Severity == AdjudicationFindingSeverity.Error)).IsEqualTo(!truncated);
    }

    [Test]
    public async Task Distinct_legitimate_adjudications_can_each_have_a_blood_event()
    {
        var first = Decision(1, GameplayFactResult.Correct);
        var wrong = Decision(2, GameplayFactResult.Wrong);
        var last = Decision(3, GameplayFactResult.Correct);
        var evidence = Evidence(first, wrong, last,
            new(Guid.NewGuid(), first.OccurredAt, CompetitionEventKind.FirstBloodAwarded, null, GameplayFactResult.Correct, ParentEventId: first.EventId),
            new(Guid.NewGuid(), last.OccurredAt, CompetitionEventKind.FirstBloodAwarded, null, GameplayFactResult.Correct, ParentEventId: last.EventId)) with
        {
            HasEarlierCorrect = false, RecordedBloodRanks = [LeaderboardBloodRank.First, LeaderboardBloodRank.First]
        };
        var result = HistoricalAdjudicationAnalyzer.Analyze(evidence);
        await Assert.That(result.Differences.All(item => item.Severity == AdjudicationFindingSeverity.Information)).IsTrue();
        var duplicate = evidence with { Events = evidence.Events.Select(item => HistoricalAdjudicationAnalyzer.IsBlood(item.Kind)
            ? item with { ParentEventId = last.EventId } : item).ToArray() };
        await Assert.That(HistoricalAdjudicationAnalyzer.Analyze(duplicate).Differences.Any(item =>
            item.Kind == AdjudicationDifferenceKind.DuplicateBloodAward && item.Severity == AdjudicationFindingSeverity.Error)).IsTrue();
    }

    [Test]
    public async Task Known_eligibility_adjustments_are_information_and_not_historical_corruption()
    {
        var adjudication = Decision(1, GameplayFactResult.Correct);
        var result = HistoricalAdjudicationAnalyzer.Analyze(Evidence(adjudication,
            new(Guid.NewGuid(), At.AddSeconds(1), CompetitionEventKind.FirstBloodAwarded, null, GameplayFactResult.Correct, ParentEventId: adjudication.EventId)) with
        {
            RecordedBloodRanks = [LeaderboardBloodRank.First], CurrentBloodEligible = false,
            BloodEligibilityHistoryRequiresReview = true, HasEligibilityChanges = true
        });
        await Assert.That(result.CurrentProjectedBloodRank).IsNull();
        await Assert.That(result.DeterministicExpectedBloodRank).IsNull();
        await Assert.That(result.Differences.All(item => item.Severity == AdjudicationFindingSeverity.Information)).IsTrue();
        await Assert.That(result.Differences.Any(item => item.Classification == AdjudicationFindingClassification.EligibilityAdjustment)).IsTrue();
    }

    [Test]
    public async Task Legacy_events_without_processing_state_remain_incomplete_evidence()
    {
        var result = HistoricalAdjudicationAnalyzer.Analyze(Evidence(Decision(1, GameplayFactResult.Wrong, null)));
        await Assert.That(result.EvidenceCompleteness).IsEqualTo(AdjudicationEvidenceCompleteness.MissingFields);
        await Assert.That(result.Differences.All(item => item.Certainty == AdjudicationDifferenceCertainty.NeedsReview)).IsTrue();
    }

    [Test, Arguments(GameplayFactResult.Correct), Arguments(GameplayFactResult.Wrong)]
    public async Task Ordinary_nonparticipants_without_awards_or_adjustments_do_not_need_review(GameplayFactResult result)
    {
        var analyzed = HistoricalAdjudicationAnalyzer.Analyze(Evidence(Decision(1, result)) with
        {
            CurrentResult = result, CurrentBloodEligible = false, HasEarlierCorrect = false,
            BloodEligibilityHistoryRequiresReview = true, HasEligibilityChanges = false, RecordedBloodRanks = []
        });
        await Assert.That(analyzed.CurrentProjectedBloodRank).IsNull();
        await Assert.That(analyzed.Differences).IsEmpty();
        await Assert.That(analyzed.EvidenceCompleteness).IsEqualTo(AdjudicationEvidenceCompleteness.Complete);
    }
}
