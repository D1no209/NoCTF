using FluentStorage.Storage;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.LiveSolo;
using NoCTF.Infrastructure.LiveSolo.Media;
using NoCTF.Infrastructure.Teams.Moderation;
using NoCTF.Tests.Integration.Persistence;

namespace NoCTF.Tests.Integration.LiveSolo;

[Category("Integration")]
public sealed class LiveSoloProgramReaderPersistenceTests
{
    [Test, Timeout(300_000)]
    public async Task Unpublished_video_and_state_are_unreadable_and_the_published_frame_never_uses_current_scores(CancellationToken ct)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var fixture = await LiveSoloMatchPersistenceTests.Fixture.CreateAsync(ct); await fixture.PrepareAsync(ct); await fixture.StartAsync(ct);
            var segment = await fixture.Db.LiveSoloProgramSegments.SingleAsync(ct);
            var objects = Substitute.For<IStore>(); objects.ObjectExists(Arg.Any<string>(), ct).Returns(true);
            objects.OpenRead(Arg.Any<string>(), ct).Returns(_ => Task.FromResult<Stream?>(new MemoryStream([0x47, 1, 2, 3])));
            var reader = new LiveSoloProgramReader(fixture.Db, new CompetitionModerationAuthorizer(fixture.Db), objects, fixture.Clock);
            await Assert.That(await reader.ReadAsync(fixture.Competition.Id, fixture.Match.Id, Guid.Empty, ct)).IsNull();
            await Assert.That(await reader.OpenSegmentAsync(fixture.Competition.Id, fixture.Match.Id, segment.Id, Guid.Empty, ct)).IsNull();
            await objects.DidNotReceive().OpenRead(Arg.Any<string>(), ct);
            var match = await fixture.Db.LiveSoloMatches.SingleAsync(ct); match.LeftWins = 99; await fixture.Db.SaveChangesAsync(ct);
            fixture.Now = segment.PublicAt;
            var result = await reader.ReadAsync(fixture.Competition.Id, fixture.Match.Id, Guid.Empty, ct);
            await Assert.That(result).IsNotNull(); await Assert.That(result!.State.LeftWins).IsEqualTo(0);
            await Assert.That(result.Segments.Single().State.AsOf).IsEqualTo(result.State.AsOf);
            var content = await reader.OpenSegmentAsync(fixture.Competition.Id, fixture.Match.Id, segment.Id, Guid.Empty, ct);
            await Assert.That(content).IsNotNull(); await content!.Content.DisposeAsync();
            await Assert.That(await reader.OpenSegmentAsync(Guid.NewGuid(), fixture.Match.Id, segment.Id, Guid.Empty, ct)).IsNull();
            fixture.Now = segment.RemoveAfter;
            await Assert.That(await reader.OpenSegmentAsync(fixture.Competition.Id, fixture.Match.Id, segment.Id, Guid.Empty, ct)).IsNull();
        });
    }
    [Test, Timeout(300_000)]
    public async Task Private_competitions_and_disabled_modes_do_not_return_program_data(CancellationToken ct)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var fixture = await LiveSoloMatchPersistenceTests.Fixture.CreateAsync(ct); await fixture.PrepareAsync(ct); await fixture.StartAsync(ct);
            fixture.Now = (await fixture.Db.LiveSoloProgramSegments.SingleAsync(ct)).PublicAt;
            var competition = await fixture.Db.Competitions.SingleAsync(ct); competition.AccessMode = CompetitionAccessMode.StaffOnly;
            await fixture.Db.SaveChangesAsync(ct);
            var reader = new LiveSoloProgramReader(fixture.Db, new CompetitionModerationAuthorizer(fixture.Db), Substitute.For<IStore>(), fixture.Clock);
            await Assert.That(await reader.ReadAsync(fixture.Competition.Id, fixture.Match.Id, Guid.Empty, ct)).IsNull();
            await Assert.That(await reader.ReadAsync(fixture.Competition.Id, fixture.Match.Id, fixture.Left.Id, ct)).IsNull();
            await Assert.That(await reader.ReadAsync(fixture.Competition.Id, fixture.Match.Id, fixture.Owner.Id, ct)).IsNotNull();
            (await fixture.Db.Set<LiveSoloCompetitionModeConfiguration>().SingleAsync(ct)).Enabled = false; await fixture.Db.SaveChangesAsync(ct);
            await Assert.That(await reader.ReadAsync(fixture.Competition.Id, fixture.Match.Id, fixture.Owner.Id, ct)).IsNull();
        });
    }
}
