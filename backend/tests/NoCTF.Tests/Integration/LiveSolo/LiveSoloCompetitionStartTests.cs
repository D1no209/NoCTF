using Microsoft.EntityFrameworkCore;
using NSubstitute;
using NoCTF.Application.Competitions.Lifecycle;
using NoCTF.Application.Messaging;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.LiveSolo;
using NoCTF.GameModes.Registration;
using NoCTF.Infrastructure.Competitions.Lifecycle;
using NoCTF.Tests.Integration.Persistence;

namespace NoCTF.Tests.Integration.LiveSolo;

[Category("Integration")]
public sealed class LiveSoloCompetitionStartTests
{
    [Test, Timeout(300_000)]
    [Arguments(false)] [Arguments(true)]
    public Task Unpublished_question_list_can_be_prepared_then_started_by_manual_or_scheduled_competition_transition(bool automatic, CancellationToken ct) =>
        DockerIntegrationTest.RunAsync(async () =>
        {
            await using var f = await LiveSoloMatchPersistenceTests.Fixture.CreateAsync(ct, false);
            f.Competition.Status = CompetitionStatus.Published;
            f.Competition.Tracks = CompetitionTrackConfiguration.ToPersisted(CompetitionTrackConfiguration.DefaultFor(GameMode.LiveSolo), f.Competition.Id);
            await f.Db.SaveChangesAsync(ct);
            await f.PrepareAsync(ct);
            await Assert.That(await f.Db.CompetitionChallenges.AnyAsync(x => x.IsPublished, ct)).IsFalse();
            var gate = new CompetitionStartGate(new CompetitionStartGateStore(f.Db),
                new GameModeCompetitionConfigurationValidator(), new GameModeChallengeConfigurationCatalog());
            await Assert.That((await gate.ValidateAsync(f.Competition.Id, ct))!.Count).IsEqualTo(0);
            var lifecycle = new CompetitionLifecycleStore(f.Db, gate, Substitute.For<IPostCommitMessagePublisher>(), clock: f.Clock);
            if (automatic)
            {
                var transitions = await new AdvanceCompetitionLifecycleUseCase(lifecycle, gate).ExecuteAsync(f.Now, ct);
                await Assert.That(transitions.Any(x => x.CompetitionId == f.Competition.Id && x.To == CompetitionStatus.Running)).IsTrue();
            }
            else
            {
                var result = await new TransitionCompetitionLifecycle(lifecycle, f.Clock, startGate: gate)
                    .ExecuteAsync(f.Competition.Id, CompetitionStatus.Running, f.Owner.Id, null, ct);
                await Assert.That(result.Succeeded).IsTrue();
            }
            await Assert.That((await f.Db.Competitions.AsNoTracking().SingleAsync(ct)).Status).IsEqualTo(CompetitionStatus.Running);
            await f.StartAsync(ct);
            await Assert.That(f.Round.State).IsEqualTo(LiveSoloRoundState.Running);
            await Assert.That(f.Round.Questions.Count(x => x.OpenedAt != null)).IsEqualTo(1);
            await Assert.That(await f.Db.CompetitionChallenges.AnyAsync(x => x.IsPublished, ct)).IsFalse();
        });
}
