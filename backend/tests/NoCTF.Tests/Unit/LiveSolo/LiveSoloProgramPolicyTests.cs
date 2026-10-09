using NoCTF.Application.LiveSolo.Media;

namespace NoCTF.Tests.Unit.LiveSolo;

public sealed class LiveSoloProgramPolicyTests
{
    [Test]
    public async Task Provider_timestamps_cannot_release_video_earlier_than_the_first_server_observation_plus_delay()
    {
        var now = DateTimeOffset.UtcNow;
        await Assert.That(LiveSoloProgramPolicy.PublicationTime(now.AddHours(-1), now, 60)).IsEqualTo(now.AddSeconds(60));
        await Assert.That(LiveSoloProgramPolicy.PublicationTime(now.AddSeconds(2), now, 60)).IsEqualTo(now.AddSeconds(62));
        await Assert.That(LiveSoloProgramPolicy.MayReadSegment(now.AddSeconds(60), now.AddMinutes(5), now.AddSeconds(59))).IsFalse();
        await Assert.That(LiveSoloProgramPolicy.MayReadSegment(now.AddSeconds(60), now.AddMinutes(5), now.AddSeconds(60))).IsTrue();
        await Assert.That(LiveSoloProgramPolicy.MayReadSegment(now, now.AddMinutes(5), now.AddMinutes(5))).IsFalse();
    }
}
