using NoCTF.Application.Competitions.Lifecycle;
using NoCTF.Application.LiveSolo.Rounds;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.LiveSolo;
using NoCTF.Infrastructure.Messaging;

namespace NoCTF.Tests.Unit.LiveSolo;

public sealed class LiveSoloCompetitionPauseTests
{
    [Test]
    public async Task Competition_pause_identity_is_round_scoped_and_overlapping_match_pauses_are_counted_once()
    {
        var now = DateTimeOffset.UtcNow; var eventId = Guid.NewGuid(); var round = Guid.NewGuid();
        var moments = new[] { new CompetitionLifecycleMoment(eventId, now.AddSeconds(10), CompetitionStatus.Running, CompetitionStatus.Paused),
            new CompetitionLifecycleMoment(Guid.NewGuid(), now.AddSeconds(30), CompetitionStatus.Paused, CompetitionStatus.Running) };
        var projected = LiveSoloCompetitionPausePolicy.Project(round, now, now.AddSeconds(60), moments);
        var replayed = LiveSoloCompetitionPausePolicy.Project(round, now, now.AddSeconds(60), moments);
        var other = LiveSoloCompetitionPausePolicy.Project(Guid.NewGuid(), now, now.AddSeconds(60), moments);
        await Assert.That(projected.Single().Id).IsEqualTo(replayed.Single().Id);
        await Assert.That(projected.Single().Id).IsNotEqualTo(other.Single().Id);
        var pauses = projected.Append(new LiveSoloPauseInterval { Source = LiveSoloPauseSource.Match, StartedAt = now.AddSeconds(20), EndedAt = now.AddSeconds(40) });
        await Assert.That(LiveSoloActiveClock.Elapsed(now, now.AddSeconds(60), pauses)).IsEqualTo(TimeSpan.FromSeconds(30));
    }
    [Test]
    public async Task Open_pause_and_moved_wakeup_preserve_scope_and_revision()
    {
        var now = DateTimeOffset.UtcNow; var round = Guid.NewGuid();
        var pauses = LiveSoloCompetitionPausePolicy.Project(round, now, now.AddSeconds(60), [
            new(Guid.NewGuid(), now.AddSeconds(-10), CompetitionStatus.Running, CompetitionStatus.Paused)]);
        await Assert.That(LiveSoloActiveClock.Elapsed(now, now.AddSeconds(60), pauses)).IsEqualTo(TimeSpan.Zero);
        var entry = new ClusterScheduleEntry("round", ClusterScheduleKind.LiveSoloRound, now, TimeSpan.FromMilliseconds(500), new AdvanceLiveSoloRound(round, 7, now));
        var next = (AdvanceLiveSoloRound)entry.At(now.AddSeconds(1)).Message;
        await Assert.That(next.RoundId).IsEqualTo(round); await Assert.That(next.TimelineRevision).IsEqualTo(7);
        await Assert.That(next.At).IsEqualTo(now.AddSeconds(1));
    }
}
