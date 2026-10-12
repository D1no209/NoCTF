using NoCTF.Domain.Competitions.Awd;
using NoCTF.GameModes.Awd.Checking;

namespace NoCTF.Tests.Unit.GameModes;

public sealed class AwdHealthEvidenceTests
{
    private static readonly AwdProbe Pass = new(AwdProbeOutcome.Passed);
    private static readonly AwdProbe Missing = new(AwdProbeOutcome.TargetFailure, AwdCheckReason.HistoricalFlagMissing);

    private static AwdFlagObservation Flag(bool historical, AwdProbe read, AwdFlagObligation obligation = AwdFlagObligation.PlayerRequired) =>
        new(Guid.NewGuid(), historical, obligation, historical ? new(AwdProbeOutcome.NotRequired) : Pass, read);

    [Test]
    public async Task CurrentPassesAndRequiredHistoryMissing_IsRecovering()
    {
        var result = AwdHealthEvaluator.Evaluate(new(Pass, Pass, [Flag(false, Pass), Flag(true, Missing)]));
        await Assert.That(result.Health).IsEqualTo(AwdHealth.Recovering);
        await Assert.That(result.RequiredFlags).IsEqualTo(2);
        await Assert.That(result.ReadableFlags).IsEqualTo(1);
    }

    [Test]
    public async Task CurrentUnknownCannotProveRecovery()
    {
        var result = AwdHealthEvaluator.Evaluate(new(Pass, Pass,
            [Flag(false, new(AwdProbeOutcome.Unknown)), Flag(true, Missing)]));
        await Assert.That(result.Health).IsEqualTo(AwdHealth.InternalError);
        await Assert.That(result.IsConclusive).IsFalse();
    }

    [Test]
    public async Task ExplicitlyExemptHistoricalNonDeliveryIsNotPlayerDataLoss()
    {
        var result = AwdHealthEvaluator.Evaluate(new(Pass, Pass,
            [Flag(false, Pass), Flag(true, new(AwdProbeOutcome.Unknown), AwdFlagObligation.Exempted)]));
        await Assert.That(result.Health).IsEqualTo(AwdHealth.Ok);
        await Assert.That(result.RequiredFlags).IsEqualTo(1);
    }

    [Test]
    public async Task PendingHistoricalDeliveryIsNotAnExemptionOrFinalFullSla()
    {
        var result = AwdHealthEvaluator.Evaluate(new(Pass, Pass,
            [Flag(false, Pass), Flag(true, new(AwdProbeOutcome.Unknown), AwdFlagObligation.PlatformPending)]));
        await Assert.That(result.Health).IsEqualTo(AwdHealth.InternalError);
        await Assert.That(result.IsConclusive).IsFalse();
        await Assert.That(NoCTF.GameModes.Awd.Scoring.AwdScoreCalculator.SlaRatio(
            new() { SlaBasis = AwdSlaBasis.ReadableFlags }, result.Health, result.RequiredFlags, result.ReadableFlags)).IsNull();
    }

    [Test]
    public async Task PlatformFailurePreservesIndependentProvenTargetFailure()
    {
        var result = AwdHealthEvaluator.Evaluate(new(Pass,
            new(AwdProbeOutcome.TargetFailure, AwdCheckReason.FunctionalCheckFailed),
            [Flag(true, new(AwdProbeOutcome.PlatformFailure, AwdCheckReason.InfrastructureFailure))]));
        await Assert.That(result.Health).IsEqualTo(AwdHealth.InternalError);
        await Assert.That(result.KnownTargetFailure).IsEqualTo(AwdHealth.Mumble);
        await Assert.That(result.IsConclusive).IsFalse();
    }

    [Test]
    public async Task PendingPlatformDeliveryAndProvenOfflineKeepBothPiecesOfEvidence()
    {
        var result = AwdHealthEvaluator.Evaluate(new(
            new(AwdProbeOutcome.TargetFailure, AwdCheckReason.ConnectionRefused),
            new(AwdProbeOutcome.Unknown),
            [Flag(true, new(AwdProbeOutcome.Unknown), AwdFlagObligation.PlatformPending)]));
        await Assert.That(result.Health).IsEqualTo(AwdHealth.InternalError);
        await Assert.That(result.KnownTargetFailure).IsEqualTo(AwdHealth.Offline);
    }

    [Test]
    public async Task ProvenFunctionalityFailureDoesNotSilentlyDiscardUnobservedHistoricalStore()
    {
        var result = AwdHealthEvaluator.Evaluate(new(Pass,
            new(AwdProbeOutcome.TargetFailure, AwdCheckReason.FunctionalCheckFailed),
            [Flag(true, new(AwdProbeOutcome.Unknown))]));
        await Assert.That(result.Health).IsEqualTo(AwdHealth.InternalError);
        await Assert.That(result.KnownTargetFailure).IsEqualTo(AwdHealth.Mumble);
    }

    [Test]
    [Arguments(AwdProbeOutcome.TargetFailure, AwdHealth.Offline)]
    [Arguments(AwdProbeOutcome.Unknown, AwdHealth.InternalError)]
    [Arguments(AwdProbeOutcome.PlatformFailure, AwdHealth.InternalError)]
    public async Task ConnectionOutcomeRequiresAttribution(AwdProbeOutcome outcome, AwdHealth expected)
    {
        var result = AwdHealthEvaluator.Evaluate(new(new(outcome, AwdCheckReason.ConnectTimeout),
            new(AwdProbeOutcome.Unknown), []));
        await Assert.That(result.Health).IsEqualTo(expected);
    }

    [Test]
    public async Task CurrentFlagFailureIsMumbleEvenWithMissingHistory()
    {
        var result = AwdHealthEvaluator.Evaluate(new(Pass, Pass, [Flag(false, Missing), Flag(true, Missing)]));
        await Assert.That(result.Health).IsEqualTo(AwdHealth.Mumble);
    }

    [Test]
    public async Task CallbackCannotOmitDuplicateOrReplaceExpectedFlagIdentities()
    {
        var flagId = Guid.NewGuid();
        AwdExpectedFlag[] expected = [new(flagId, false, AwdFlagObligation.PlayerRequired, true)];
        AwdReportedFlag[][] invalid = [[], [new(Guid.NewGuid(), Pass, Pass)], [new(flagId, Pass, Pass), new(flagId, Pass, Pass)]];
        foreach (var reported in invalid)
        {
            var result = AwdHealthEvaluator.ValidateManifest(Pass, Pass, expected, reported);
            await Assert.That(result.Conclusion.Health).IsEqualTo(AwdHealth.InternalError);
            await Assert.That(result.Evidence).IsNull();
        }
    }

    [Test]
    public async Task CallbackCannotReclassifyCurrentFlagOrExemptItsRequiredWrite()
    {
        var id = Guid.NewGuid();
        AwdExpectedFlag[] expected = [new(id, false, AwdFlagObligation.PlayerRequired, true)];
        var invalid = AwdHealthEvaluator.ValidateManifest(Pass, Pass, expected,
            [new(id, new(AwdProbeOutcome.NotRequired), Pass)]);
        await Assert.That(invalid.Conclusion.Reason).IsEqualTo(AwdCheckReason.InvalidEvidence);
        var currentMissing = AwdHealthEvaluator.ValidateManifest(Pass, Pass, expected, [new(id, Pass, Missing)]);
        await Assert.That(currentMissing.Conclusion.Health).IsEqualTo(AwdHealth.Mumble);
    }

    [Test]
    public async Task Passed_probe_cannot_also_claim_checker_timeout()
    {
        var invalid = AwdHealthEvaluator.ValidateManifest(new(AwdProbeOutcome.Passed, AwdCheckReason.CheckerTimedOut), Pass, [], []);
        await Assert.That(invalid.Conclusion.Reason).IsEqualTo(AwdCheckReason.InvalidEvidence);
    }
}
