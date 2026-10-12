using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Competitions.Awd;
using NoCTF.Application.GameplayFacts.Intake;
using NoCTF.Application.Messaging;
using NoCTF.Domain.Competitions.Awd;
using NoCTF.Domain.Competitions.Events;
using NoCTF.Domain.Gameplay;
using NoCTF.Infrastructure.Competitions.Awd;
using NoCTF.Infrastructure.Competitions.Events;
using NoCTF.Infrastructure.Persistence;
using NoCTF.Infrastructure.GameplayFacts.Intake;

namespace NoCTF.Tests.Integration.Persistence;

public sealed partial class AwdRoundCoordinationTests
{
    private static async Task VerifyAdjudicationsAsync(DbContextOptions<NoCtfDbContext> options,
        Fixture fixture, Guid lateId, Guid exemptId, Guid missingId, DateTimeOffset at, CancellationToken ct)
    {
        async Task<AwdCheckEvidenceView> Read(Guid id)
        {
            await using var db = new NoCtfDbContext(options);
            var publisher = new RecordingOutbox();
            return (await new AwdCheckAdjudicationStore(db, new CompetitionEventStore(db, publisher), publisher)
                .ReadAsync(fixture.CompetitionId, id, ct))!;
        }
        async Task<AwdCheckAdjudicationResult> Apply(AwdCheckAdjudicationCommand command)
        {
            await using var db = new NoCtfDbContext(options);
            var publisher = new RecordingOutbox();
            var result = await new AdjudicateAwdCheck(new AwdCheckAdjudicationStore(db,
                new CompetitionEventStore(db, publisher), publisher)).ExecuteAsync(command, ct);
            if (result.State == AwdCheckAdjudicationState.Applied)
                await Assert.That(publisher.Published.OfType<ProjectLeaderboard>().Count()).IsEqualTo(1);
            return result;
        }
        var late = await Read(lateId);
        var command = new AwdCheckAdjudicationCommand(fixture.CompetitionId, lateId,
            late.Evidence.AttemptId, late.Evidence.ReceivedAt, fixture.OwnerId, AwdCheckAdjudicationAction.AcceptEvidence,
            "Reviewed original in-window observation; transport callback was delayed.", at);
        await Assert.That((await Apply(command with { ExpectedAttemptId = Guid.NewGuid() })).State)
            .IsEqualTo(AwdCheckAdjudicationState.Conflict);
        await Assert.That((await Apply(command with { Reason = " " })).State)
            .IsEqualTo(AwdCheckAdjudicationState.InvalidRequest);
        var concurrent = await Task.WhenAll(Apply(command), Apply(command));
        await Assert.That(concurrent.Count(x => x.State == AwdCheckAdjudicationState.Applied)).IsEqualTo(1);
        await Assert.That(concurrent.Count(x => x.State == AwdCheckAdjudicationState.Conflict)).IsEqualTo(1);
        var accepted = await Read(lateId);
        await Assert.That(accepted.Evidence.Health).IsEqualTo(AwdHealth.Ok);
        await Assert.That(accepted.Evidence.Settlement).IsEqualTo(AwdSettlementState.Final);
        await Assert.That(accepted.Evidence.AdjudicatedAt!.Value.ToUnixTimeMilliseconds()).IsEqualTo(at.ToUnixTimeMilliseconds());
        await Assert.That(accepted.Evidence.ObservedAt).IsEqualTo(late.Evidence.ObservedAt);
        await Assert.That(accepted.Evidence.ReceivedAt).IsEqualTo(late.Evidence.ReceivedAt);
        var missing = await Read(missingId);
        await Assert.That((await Apply(command with { GameplayFactId = missingId,
            ExpectedAttemptId = missing.Evidence.AttemptId, ExpectedReceivedAt = missing.Evidence.ReceivedAt })).State).IsEqualTo(AwdCheckAdjudicationState.InvalidEvidence);
        var exempt = await Read(exemptId);
        await Assert.That((await Apply(command with { GameplayFactId = exemptId,
            ExpectedAttemptId = exempt.Evidence.AttemptId, ExpectedReceivedAt = exempt.Evidence.ReceivedAt, Action = AwdCheckAdjudicationAction.Exempt,
            Reason = "Runner report unavailable; explicitly exempt this sample." })).State).IsEqualTo(AwdCheckAdjudicationState.Applied);
        await using var verify = new NoCtfDbContext(options);
        var exempted = await verify.GameplayFacts.Include(x => x.Awd).SingleAsync(x => x.Id == exemptId, ct);
        await Assert.That(exempted.Result).IsNull();
        await Assert.That(exempted.Awd!.Health).IsEqualTo(AwdHealth.InternalError);
        await Assert.That(exempted.Awd.Settlement).IsEqualTo(AwdSettlementState.Exempted);
        var audits = await verify.CompetitionEvents.Where(x => x.AwdAdjudicationAction != null).ToArrayAsync(ct);
        await Assert.That(audits.Length).IsEqualTo(2);
        await Assert.That(audits.All(x => x.ActorUserId == fixture.OwnerId && x.Reason!.Length > 0
            && x.Visibility == CompetitionEventVisibility.Staff
            && x.PreviousAwdSettlement == AwdSettlementState.PendingAdjudication)).IsTrue();
        await Assert.That(audits.Single(x => x.GameplayFactId == lateId).PreviousAwdHealth).IsEqualTo(AwdHealth.InternalError);
        await Assert.That(audits.Single(x => x.GameplayFactId == lateId).AwdHealth).IsEqualTo(AwdHealth.Ok);
        var adjustmentPublisher = new RecordingOutbox();
        var adjustments = new CreateManualAdjustment(new GameplayFactIntakeStore(verify, adjustmentPublisher,
            new CompetitionEventStore(verify, adjustmentPublisher)));
        var adjustment = new ManualAdjustmentCommand(fixture.CompetitionId, fixture.TeamId,
            fixture.CompetitionChallengeId, -25, fixture.OwnerId, at);
        await Assert.That((await adjustments.ExecuteAsync(adjustment, ct)).FailureCode)
            .IsEqualTo(GameplayFactAdmissionFailureCode.ManualAdjustmentReasonInvalid);
        var applied = await adjustments.ExecuteAsync(adjustment with { Reason = "Manual correction after evidence review" }, ct);
        await Assert.That(applied.Succeeded).IsTrue();
        var adjustmentFact = await verify.GameplayFacts.SingleAsync(x => x.Id == applied.Value!.GameplayFactId, ct);
        await Assert.That(adjustmentFact.Value).IsEqualTo("-25");
        var adjustmentAudit = await verify.CompetitionEvents.SingleAsync(x => x.GameplayFactId == adjustmentFact.Id, ct);
        await Assert.That(adjustmentAudit.Reason).IsEqualTo("Manual correction after evidence review");
        await Assert.That(adjustmentAudit.ActorUserId).IsEqualTo(fixture.OwnerId);
    }
}
