using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using NSubstitute;
using NoCTF.Application.LiveSolo.Media;
using NoCTF.Application.LiveSolo.Resources;
using NoCTF.Application.Messaging;
using NoCTF.Domain.LiveSolo;
using NoCTF.Infrastructure.LiveSolo.Matches;
using NoCTF.Infrastructure.Persistence;
using NoCTF.Infrastructure.Teams.Moderation;
using NoCTF.Domain.Challenges;
using NoCTF.Domain.Runtime;
using NoCTF.Application.Runtime.Access;
using NoCTF.Application.Runtime.Provisioning;
using NoCTF.Application.Runtime.Instances;
using NoCTF.Application.Challenges.Flags;
using NoCTF.Infrastructure.Runtime.Instances;
using NoCTF.Infrastructure.Runtime.Access;
using NoCTF.GameModes.Registration;

namespace NoCTF.Tests.Integration.Persistence;

[Category("Integration")]
public sealed class LiveSoloEffectiveTimePersistenceTests
{
    [Test, Timeout(300_000)]
    public async Task Countdown_reads_actual_time_after_media_observation_and_again_after_a_transaction_retry(CancellationToken ct)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var fixture = await LiveSoloMatchPersistenceTests.Fixture.CreateAsync(ct);
            await fixture.PrepareAsync(ct);
            var start = fixture.Now; var control = fixture.Store(fixture.Db);
            var left = await control.ConfirmReadyAsync(fixture.Competition.Id, fixture.Match.Id, fixture.Left.Id, fixture.Match.ConcurrencyStamp, fixture.Now, ct);
            var right = await control.ConfirmReadyAsync(fixture.Competition.Id, fixture.Match.Id, fixture.Right.Id, left.Match!.ConcurrencyStamp, fixture.Now, ct);
            await Assert.That(right.Failure).IsNull();
            fixture.media.ObserveAsync(Arg.Any<string>(), ct).Returns(_ =>
            {
                fixture.Clock.Advance(TimeSpan.FromSeconds(20));
                return new LiveSoloRoomObservation([new("left-screen", LiveSoloScreenState.Sharing, "l", fixture.Now),
                    new("right-screen", LiveSoloScreenState.Sharing, "r", fixture.Now)]);
            });
            var retry = new DelayAndRetry(fixture.Clock);
            await using var db = new NoCtfDbContext(new DbContextOptionsBuilder<NoCtfDbContext>(fixture.Options).AddInterceptors(retry).Options);
            var store = new LiveSoloMatchStore(db, new CompetitionModerationAuthorizer(db), fixture.media,
                Substitute.For<ILiveSoloRuntimePreparation>(), Substitute.For<IPostCommitMessagePublisher>(), clock: fixture.Clock);
            retry.Armed = true;
            var result = await store.StartCountdownAsync(new(fixture.Competition.Id, fixture.Match.Id, fixture.Round.Id, fixture.Owner.Id,
                fixture.Round.ConcurrencyStamp, start), ct);
            await Assert.That(result.Failure).IsNull();
            await Assert.That(result.Round!.CountdownAt).IsEqualTo(start.AddSeconds(27));
            await Assert.That(retry.Retries).IsEqualTo(1);
            await Assert.That((await db.LiveSoloRounds.AsNoTracking().SingleAsync(ct)).CountdownAt).IsEqualTo(fixture.Now);
        });
    }

    [Test, Timeout(300_000)]
    public async Task Slow_preparation_cannot_backfill_opening_or_release_a_question_after_the_actual_deadline(CancellationToken ct)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var fixture = await LiveSoloMatchPersistenceTests.Fixture.CreateAsync(ct);
            await fixture.PrepareAsync(ct); await fixture.StartAsync(ct);
            var started = fixture.Round.StartedAt!.Value;
            fixture.Now = started.AddSeconds(10);
            var scheduledAt = fixture.Now;
            var prepare = Substitute.For<ILiveSoloRuntimePreparation>();
            prepare.PrepareAsync(Arg.Any<Guid>(), Arg.Any<DateTimeOffset>(), ct).Returns(_ =>
            { fixture.Clock.Advance(TimeSpan.FromSeconds(25)); return (NoCTF.Application.LiveSolo.Rounds.LiveSoloFailure?)null; });
            var store = new LiveSoloMatchStore(fixture.Db, new CompetitionModerationAuthorizer(fixture.Db), fixture.media, prepare,
                Substitute.For<IPostCommitMessagePublisher>(), clock: fixture.Clock);
            await store.TickAsync(fixture.Round.Id, fixture.Round.TimelineRevision, scheduledAt, ct);
            await Assert.That(fixture.Round.State).IsEqualTo(LiveSoloRoundState.TimedOut);
            await Assert.That(fixture.Round.Questions.Single(x => x.Position == 1).OpenedAt).IsNull();
            await Assert.That(fixture.Round.EndedAt).IsEqualTo(started.AddSeconds(35));
            await Assert.That(fixture.Round.Questions.Single(x => x.Position == 0).OpenedAt).IsEqualTo(started);
        });
    }

    [Test, Timeout(300_000)]
    public async Task Wsrx_access_mode_cannot_certify_an_existing_or_new_execution_runtime(CancellationToken ct)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var fixture = await LiveSoloMatchPersistenceTests.Fixture.CreateAsync(ct);
            fixture.Templates[0].Definition!.Runtime = new ContainerChallengeRuntimeTemplate { Allocation = PersistedRuntimeAllocation.PerTeam,
                FlagSource = PersistedRuntimeFlagSource.Static, Services = [new() { Name = "web", Image = "example/web:test" }] };
            await fixture.Db.SaveChangesAsync(ct);
            var placement = Substitute.For<IRuntimePlacementPolicy>(); placement.Resolve(RuntimeKind.Container).Returns(new RuntimePlacement(RuntimeProvider.Docker, "test"));
            var control = new ScopedRuntimeControl(fixture.Db, new ChallengeRuntimeTemplateCatalog(), placement,
                Substitute.For<IPerTeamRuntimeFlagStore>(), Substitute.For<IPostCommitMessagePublisher>(), new UnverifiedExecutionRuntimeIsolation());
            var request = new ScopedRuntimeRequest(fixture.Competition.Id, fixture.Entries[0].Id, Guid.NewGuid(), fixture.LeftTeam.Id, fixture.Now);
            var unavailable = await control.EnsureAsync(request, ct);
            await Assert.That(unavailable.IsolationAvailable).IsFalse();
            await Assert.That(await fixture.Db.RuntimeInstances.CountAsync(ct)).IsEqualTo(0);
            var existing = new PlayerRuntimeInstance { Id = Guid.NewGuid(), CompetitionId = fixture.Competition.Id, CompetitionChallengeId = fixture.Entries[0].Id,
                TeamId = fixture.LeftTeam.Id, ExecutionScopeId = request.ExecutionScopeId, State = RuntimeState.Running, RuntimeKind = RuntimeKind.Container,
                RuntimeProvider = RuntimeProvider.Docker, AccessMode = RuntimeAccessMode.WsrxOnly, CreatedAt = fixture.Now, RunningAt = fixture.Now };
            fixture.Db.RuntimeInstances.Add(existing); await fixture.Db.SaveChangesAsync(ct);
            var stillUnverified = await control.EnsureAsync(request, ct);
            await Assert.That(stillUnverified.RuntimeInstanceId).IsEqualTo(existing.Id);
            await Assert.That(stillUnverified.IsolationAvailable).IsFalse();
        });
    }

    [Test, Timeout(300_000)]
    public async Task A_group_that_exceeds_maximum_simultaneous_runtime_quota_is_rejected_before_prewarming(CancellationToken ct)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var fixture = await LiveSoloMatchPersistenceTests.Fixture.CreateAsync(ct);
            foreach (var template in fixture.Templates)
                template.Definition!.Runtime = new ContainerChallengeRuntimeTemplate { Allocation = PersistedRuntimeAllocation.PerTeam,
                    FlagSource = PersistedRuntimeFlagSource.Static, Services = [new() { Name = "web", Image = "example/web:test" }] };
            fixture.Competition.MaxConcurrentRuntimeInstancesPerTeam = 1; await fixture.Db.SaveChangesAsync(ct);
            await fixture.PrepareAsync(ct, expectedPreparationFailure: NoCTF.Application.LiveSolo.Rounds.LiveSoloFailure.InvalidConfiguration);
            await Assert.That(await fixture.Db.LiveSoloRounds.CountAsync(ct)).IsEqualTo(0);
            await Assert.That(await fixture.Db.RuntimeInstances.CountAsync(ct)).IsEqualTo(0);
        });
    }

    private sealed class DelayAndRetry(Microsoft.Extensions.Time.Testing.FakeTimeProvider clock) : DbTransactionInterceptor
    {
        public bool Armed { get; set; }
        public int Retries { get; private set; }
        public override ValueTask<InterceptionResult> TransactionCommittingAsync(DbTransaction transaction, TransactionEventData eventData,
            InterceptionResult result, CancellationToken cancellationToken = default)
        {
            if (Armed) { Armed = false; Retries++; clock.Advance(TimeSpan.FromSeconds(7)); throw new DbUpdateConcurrencyException("Injected delayed retry"); }
            return ValueTask.FromResult(result);
        }
    }
}
