using NoCTF.Application.Messaging;
using NoCTF.Infrastructure.Messaging;
using NSubstitute;

namespace NoCTF.Tests.Unit.Infrastructure;

public sealed class ClusterScheduleTests
{
    [Test]
    public async Task LiveSolo_schedule_messages_have_explicit_typed_publication_routes()
    {
        var bus=NSubstitute.Substitute.For<Wolverine.IMessageBus>();var id=Guid.NewGuid();
        object[] messages=[new NoCTF.Application.LiveSolo.Media.RefreshLiveSoloMedia(id,"room"),
            new NoCTF.Application.LiveSolo.Media.AdvanceLiveSoloCapture(id),new NoCTF.Application.LiveSolo.Media.PruneLiveSoloCapture(id),
            new NoCTF.Application.LiveSolo.Media.SnapshotLiveSoloResult(id)];
        foreach(var message in messages)await MaintenanceTickAgent.PublishMessageAsync(bus,message);
        await Assert.That(bus.ReceivedCalls().Count()).IsEqualTo(4);
    }
    [Test]
    public async Task LiveSolo_media_and_capture_ticks_keep_their_exact_scope_when_retimed()
    {
        var id=Guid.NewGuid();var now=DateTimeOffset.UtcNow;
        object[] messages=[new NoCTF.Application.LiveSolo.Media.RefreshLiveSoloMedia(id,"opaque-room"),
            new NoCTF.Application.LiveSolo.Media.AdvanceLiveSoloCapture(id),new NoCTF.Application.LiveSolo.Media.PruneLiveSoloCapture(id),
            new NoCTF.Application.LiveSolo.Media.SnapshotLiveSoloResult(id)];
        foreach(var message in messages)
        {
            var moved=new ClusterScheduleEntry("media",ClusterScheduleKind.LiveSoloCapture,now,TimeSpan.FromSeconds(2),message).At(now.AddSeconds(10));
            await Assert.That(moved.Message).IsSameReferenceAs(message);await Assert.That(moved.DueAt).IsEqualTo(now.AddSeconds(10));
        }
        var round=new NoCTF.Application.LiveSolo.Rounds.AdvanceLiveSoloRound(id,7,now);
        var next=new ClusterScheduleEntry("round",ClusterScheduleKind.LiveSoloRound,now,null,round).At(now.AddSeconds(10));
        await Assert.That(((NoCTF.Application.LiveSolo.Rounds.AdvanceLiveSoloRound)next.Message).At).IsEqualTo(now.AddSeconds(10));
        await Assert.That(((NoCTF.Application.LiveSolo.Rounds.AdvanceLiveSoloRound)next.Message).TimelineRevision).IsEqualTo(7);
    }
    [Test]
    public async Task Clamp_skips_past_ticks_without_replaying_them()
    {
        var now = new DateTimeOffset(2026, 8, 24, 12, 0, 0, TimeSpan.Zero);
        var intended = now.AddSeconds(-35);

        var result = ClusterScheduleClock.ClampWithoutCatchUp(
            intended,
            now,
            TimeSpan.FromSeconds(10));

        await Assert.That(result.DueAt).IsEqualTo(now);
        await Assert.That(result.SkippedTicks).IsEqualTo(3);
        await Assert.That(ClusterScheduleClock.NextAfterDispatch(
            now,
            TimeSpan.FromSeconds(10))).IsEqualTo(now.AddSeconds(10));
    }

    [Test]
    public async Task Moving_a_KoH_tick_changes_its_stable_fact_key()
    {
        var competitionId = Guid.CreateVersion7();
        var challengeId = Guid.CreateVersion7();
        var dueAt = new DateTimeOffset(2026, 8, 24, 12, 0, 0, TimeSpan.Zero);
        var first = PollKohChallenge.Create(
            competitionId,
            challengeId,
            dueAt.AddMinutes(-5),
            dueAt);

        var replay = PollKohChallenge.Create(
            competitionId,
            challengeId,
            dueAt.AddMinutes(-5),
            dueAt);
        var next = first.At(dueAt.AddSeconds(10));

        await Assert.That(replay.GameplayFactId).IsEqualTo(first.GameplayFactId);
        await Assert.That(next.GameplayFactId).IsNotEqualTo(first.GameplayFactId);
        await Assert.That(next.DueAt).IsEqualTo(dueAt.AddSeconds(10));
    }
}
