using NoCTF.Application.Competitions.Awd;
using NoCTF.Domain.Competitions.Awd;
using NoCTF.Domain.Gameplay;

namespace NoCTF.Tests.Unit.Application;

public sealed class AwdCheckAdjudicationTests
{
    [Test]
    [Arguments(false, true, 0, false)]
    [Arguments(true, false, 0, false)]
    [Arguments(true, true, -1, false)]
    [Arguments(true, true, 31, false)]
    [Arguments(true, true, 15, true)]
    public async Task Confirmation_requires_complete_original_window_evidence(bool manifest, bool reported,
        int observedSeconds, bool accepted)
    {
        var start = DateTimeOffset.UtcNow;
        var fact = new AwdServiceTransitionGameplayFact { Id = Guid.NewGuid(), CompetitionId = Guid.NewGuid(),
            Awd = new() { Settlement = AwdSettlementState.PendingAdjudication,
                Health = AwdHealth.InternalError, ManifestComplete = manifest, EvidenceComplete = reported,
                AttemptId = Guid.NewGuid(), AttemptStartedAt = start, AttemptDeadline = start.AddSeconds(30),
                ObservedAt = start.AddSeconds(observedSeconds), ReceivedAt = start.AddMinutes(2) } };
        var command = new AwdCheckAdjudicationCommand(fact.CompetitionId, fact.Id, fact.Awd.AttemptId, fact.Awd.ReceivedAt,
            Guid.NewGuid(), AwdCheckAdjudicationAction.AcceptEvidence, "Checked original evidence", start.AddMinutes(3));
        var result = AdjudicateAwdCheck.Apply(fact, command, new(AwdHealth.Ok, AwdCheckReason.None, null, 1, 1));
        await Assert.That(result).IsEqualTo(accepted ? AwdCheckAdjudicationState.Applied : AwdCheckAdjudicationState.InvalidEvidence);
        await Assert.That(fact.Awd.Settlement).IsEqualTo(accepted ? AwdSettlementState.Final : AwdSettlementState.PendingAdjudication);
    }
}
