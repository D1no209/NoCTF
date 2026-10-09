using Microsoft.EntityFrameworkCore;
using NSubstitute;
using NoCTF.Application.LiveSolo.Resources;
using NoCTF.Application.LiveSolo.Rounds;
using NoCTF.Application.Messaging;
using NoCTF.Application.Runtime.Instances;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Competitions.Events;
using NoCTF.Domain.LiveSolo;
using NoCTF.Infrastructure.LiveSolo.Matches;
using NoCTF.Infrastructure.LiveSolo.Resources;
using NoCTF.Infrastructure.LiveSolo.Rounds;
using NoCTF.Infrastructure.Persistence;
using NoCTF.Infrastructure.Teams.Moderation;

namespace NoCTF.Tests.Integration.Persistence;

[Category("Integration")]
public sealed class LiveSoloResumeAndReadinessPersistenceTests
{
    [Test, Timeout(300_000)]
    public async Task Resumed_competition_rebuilds_release_and_deadline_after_an_open_pause_was_persisted_without_player_actions(CancellationToken ct)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var fixture = await LiveSoloMatchPersistenceTests.Fixture.CreateAsync(ct);
            await fixture.PrepareAsync(ct); await fixture.StartAsync(ct);
            var started = fixture.Round.StartedAt!.Value;
            void Transition(DateTimeOffset at, CompetitionStatus from, CompetitionStatus to)
            {
                var item = CompetitionEventGeneratedCatalog.Create(CompetitionEventKind.CompetitionLifecycleChanged);
                item.Id = Guid.NewGuid(); item.CompetitionId = fixture.Competition.Id; item.OccurredAt = at;
                item.PreviousCompetitionStatus = from; item.CompetitionStatus = to;
                fixture.Db.CompetitionEvents.Add(item); fixture.Competition.Status = to;
            }
            Transition(started.AddSeconds(5), CompetitionStatus.Running, CompetitionStatus.Paused);
            await fixture.Db.SaveChangesAsync(ct);
            fixture.Now = started.AddSeconds(8);
            await fixture.Store(fixture.Db).TickAsync(fixture.Round.Id, fixture.Round.TimelineRevision, fixture.Now, ct);
            await Assert.That((await fixture.Db.Set<LiveSoloPauseInterval>().AsNoTracking().SingleAsync(ct)).EndedAt).IsNull();
            await using (var pausedWorker = new NoCtfDbContext(fixture.Options))
                await Assert.That(await new LiveSoloScheduleSource(pausedWorker).RebuildAsync(fixture.Now, ct)).IsEmpty();

            Transition(started.AddSeconds(35), CompetitionStatus.Paused, CompetitionStatus.Running);
            await fixture.Db.SaveChangesAsync(ct);
            fixture.Now = started.AddSeconds(40);
            await using var restartedWorker = new NoCtfDbContext(fixture.Options);
            var schedule = (await new LiveSoloScheduleSource(restartedWorker).RebuildAsync(fixture.Now, ct)).Single();
            var release = (AdvanceLiveSoloRound)schedule.Message;
            fixture.Now = schedule.DueAt;
            await fixture.Store(restartedWorker).TickAsync(release.RoundId, release.TimelineRevision, fixture.Now, ct);
            var round = await restartedWorker.LiveSoloRounds.AsNoTracking().Include(x => x.Questions).SingleAsync(ct);
            await Assert.That(round.Questions.Single(x => x.Position == 1).OpenedAt).IsEqualTo(fixture.Now);
            await Assert.That((await restartedWorker.Set<LiveSoloPauseInterval>().AsNoTracking().SingleAsync(ct)).EndedAt)
                .IsEqualTo(started.AddSeconds(35));
            var deadline = (await new LiveSoloScheduleSource(restartedWorker).RebuildAsync(fixture.Now, ct)).Single();
            await Assert.That(deadline.DueAt).IsEqualTo(started.AddSeconds(60));
            fixture.Now = deadline.DueAt;
            var timeout = (AdvanceLiveSoloRound)deadline.Message;
            await fixture.Store(restartedWorker).TickAsync(timeout.RoundId, timeout.TimelineRevision, fixture.Now, ct);
            await Assert.That((await restartedWorker.LiveSoloRounds.AsNoTracking().SingleAsync(ct)).State).IsEqualTo(LiveSoloRoundState.TimedOut);
            await Assert.That(await restartedWorker.GameplayFacts.CountAsync(ct)).IsEqualTo(0);
        });
    }

    [Test, Arguments(LiveSoloFailure.IsolationUnavailable), Arguments(LiveSoloFailure.DependencyUnavailable), Timeout(300_000)]
    public async Task First_and_later_questions_require_a_successful_current_recheck_even_if_ready_was_previously_saved(
        LiveSoloFailure failure, CancellationToken ct)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var fixture = await LiveSoloMatchPersistenceTests.Fixture.CreateAsync(ct);
            await fixture.PrepareAsync(ct);
            var control = fixture.Store(fixture.Db);
            var left = await control.ConfirmReadyAsync(fixture.Competition.Id, fixture.Match.Id, fixture.Left.Id, fixture.Match.ConcurrencyStamp, fixture.Now, ct);
            var right = await control.ConfirmReadyAsync(fixture.Competition.Id, fixture.Match.Id, fixture.Right.Id, left.Match!.ConcurrencyStamp, fixture.Now, ct);
            await Assert.That(right.Failure).IsNull();
            var countdown = await control.StartCountdownAsync(new(fixture.Competition.Id, fixture.Match.Id, fixture.Round.Id, fixture.Owner.Id,
                fixture.Round.ConcurrencyStamp, fixture.Now), ct);
            await Assert.That(countdown.Failure).IsNull();
            fixture.Now = fixture.Now.AddSeconds(fixture.Round.CountdownSeconds);
            // A failing adapter that leaves persisted Ready unchanged must still never authorize opening.
            var preparation = Substitute.For<ILiveSoloRuntimePreparation>();
            preparation.PrepareAsync(Arg.Any<Guid>(), Arg.Any<DateTimeOffset>(), ct).Returns(failure);
            var store = new LiveSoloMatchStore(fixture.Db, new CompetitionModerationAuthorizer(fixture.Db), fixture.media, fixture.egress, preparation,
                Substitute.For<IPostCommitMessagePublisher>(), clock: fixture.Clock);
            await store.TickAsync(fixture.Round.Id, fixture.Round.TimelineRevision, fixture.Now, ct);
            await Assert.That(fixture.Round.State).IsEqualTo(LiveSoloRoundState.Countdown);
            await Assert.That(fixture.Round.StartedAt).IsNull();
            await Assert.That(fixture.Round.Questions.Single(x => x.Position == 0).OpenedAt).IsNull();

            await control.TickAsync(fixture.Round.Id, fixture.Round.TimelineRevision, fixture.Now, ct);
            await Assert.That(fixture.Round.State).IsEqualTo(LiveSoloRoundState.Running);
            var later = fixture.Round.Questions.Single(x => x.Position == 1);
            later.Readiness = LiveSoloQuestionReadiness.Ready; await fixture.Db.SaveChangesAsync(ct);
            fixture.Now = fixture.Now.AddSeconds(later.OpenOffsetSeconds);
            await store.TickAsync(fixture.Round.Id, fixture.Round.TimelineRevision, fixture.Now, ct);
            await Assert.That(later.OpenedAt).IsNull();
            await Assert.That(fixture.Round.State).IsEqualTo(LiveSoloRoundState.Running);
        });
    }

    [Test, Arguments(false), Arguments(true), Timeout(300_000)]
    public async Task Failed_isolation_or_dependency_assessment_revokes_persisted_readiness(bool dependencyFailure, CancellationToken ct)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var fixture = await LiveSoloMatchPersistenceTests.Fixture.CreateAsync(ct);
            await fixture.PrepareAsync(ct);
            var question = fixture.Round.Questions.Single(x => x.Position == 0);
            await Assert.That(question.Readiness).IsEqualTo(LiveSoloQuestionReadiness.Ready);
            var stamp = question.ConcurrencyStamp;
            var runtimes = Substitute.For<IScopedRuntimeControl>();
            runtimes.EnsureAsync(Arg.Any<ScopedRuntimeRequest>(), ct).Returns(new ScopedRuntimeResult(null, null, dependencyFailure,
                dependencyFailure ? RuntimeMutationFailure.ConfigurationInvalid : null));
            var result = await new LiveSoloRuntimePreparation(fixture.Db, runtimes).PrepareAsync(question.Id, fixture.Now, ct);
            await Assert.That(result).IsEqualTo(dependencyFailure ? LiveSoloFailure.DependencyUnavailable : LiveSoloFailure.IsolationUnavailable);
            await using var read = new NoCtfDbContext(fixture.Options);
            var saved = await read.LiveSoloRoundQuestions.SingleAsync(x => x.Id == question.Id, ct);
            await Assert.That(saved.Readiness).IsEqualTo(LiveSoloQuestionReadiness.Failed);
            await Assert.That(saved.ConcurrencyStamp).IsNotEqualTo(stamp);
        });
    }

    [Test, Timeout(300_000)]
    public async Task A_dependency_exception_revokes_ready_before_propagating_the_failure(CancellationToken ct)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var fixture = await LiveSoloMatchPersistenceTests.Fixture.CreateAsync(ct);
            await fixture.PrepareAsync(ct);
            var question = fixture.Round.Questions.Single(x => x.Position == 0);
            var runtimes = Substitute.For<IScopedRuntimeControl>();
            runtimes.EnsureAsync(Arg.Any<ScopedRuntimeRequest>(), ct).Returns(Task.FromException<ScopedRuntimeResult>(new HttpRequestException("Unavailable")));
            var preparation = new LiveSoloRuntimePreparation(fixture.Db, runtimes);
            await Assert.That(async () => await preparation.PrepareAsync(question.Id, fixture.Now, ct)).Throws<HttpRequestException>();
            await using var read = new NoCtfDbContext(fixture.Options);
            await Assert.That((await read.LiveSoloRoundQuestions.SingleAsync(x => x.Id == question.Id, ct)).Readiness).IsEqualTo(LiveSoloQuestionReadiness.Failed);
        });
    }
}
