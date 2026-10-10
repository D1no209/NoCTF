using Microsoft.EntityFrameworkCore;
using NSubstitute;
using NoCTF.Application.Challenges.Flags;
using NoCTF.Application.Commands.Idempotency;
using NoCTF.Application.LiveSolo.Resources;
using NoCTF.Application.Messaging;
using NoCTF.Application.Runtime.Access;
using NoCTF.Application.Runtime.Instances;
using NoCTF.Application.Runtime.Provisioning;
using NoCTF.Domain.Challenges;
using NoCTF.Domain.Runtime;
using NoCTF.GameModes.Registration;
using NoCTF.Infrastructure.Commands.Idempotency;
using NoCTF.Infrastructure.LiveSolo.Resources;
using NoCTF.Infrastructure.Persistence;
using NoCTF.Infrastructure.Runtime.Instances;

namespace NoCTF.Tests.Integration.Persistence;

[Category("Integration")]
public sealed class LiveSoloRuntimeControlPersistenceTests
{
    [Test, Timeout(300_000)]
    public async Task Opened_environment_is_team_shared_reset_is_atomic_and_replayed_and_foreign_stop_is_denied(CancellationToken ct)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var fixture = await LiveSoloMatchPersistenceTests.Fixture.CreateAsync(ct);
            await ConfigureAsync(fixture, ct); var request = Request(fixture);
            var initial = await Store(fixture, fixture.Db, Guid.NewGuid()).MutateAsync(new(request, RuntimeAction.Start, null), ct);
            await Assert.That(initial.Failure).IsNull(); var original = initial.Runtime!;
            var key = Guid.NewGuid(); var reset = new LiveSoloRuntimeCommand(request, RuntimeAction.Reset, original.Id);
            await using var firstDb = new NoCtfDbContext(fixture.Options);
            var replaced = await Store(fixture, firstDb, key).MutateAsync(reset, ct);
            await Assert.That(replaced.Failure).IsNull();
            await Assert.That(replaced.Runtime!.Id).IsNotEqualTo(original.Id);
            await using var retryDb = new NoCtfDbContext(fixture.Options);
            var replayed = await Store(fixture, retryDb, key).MutateAsync(reset, ct);
            await Assert.That(replayed.Failure).IsNull();
            await Assert.That(replayed.Runtime!.Id).IsEqualTo(replaced.Runtime.Id);
            await Assert.That(await retryDb.RuntimeInstances.CountAsync(ct)).IsEqualTo(2);
            await Assert.That((await retryDb.RuntimeInstances.SingleAsync(x => x.Id == original.Id, ct)).State).IsEqualTo(RuntimeState.Stopping);
            await Assert.That(await retryDb.RuntimeInstances.CountAsync(x => x.ActiveSlot != null, ct)).IsEqualTo(1);
            await Assert.That((await retryDb.RuntimeInstances.SingleAsync(x => x.Id == replaced.Runtime.Id, ct)).CreatedAt).IsEqualTo(original.CreatedAt);
            await Assert.That((await Store(fixture, retryDb, Guid.NewGuid()).ReadAsync(request, ct))!.Id).IsEqualTo(replaced.Runtime.Id);
            var questions = await fixture.Store(retryDb).QuestionsAsync(fixture.Competition.Id, fixture.Match.Id, fixture.Round.Id, fixture.Left.Id, fixture.Now, ct);
            await Assert.That(questions!.Single().RuntimeInstanceId).IsEqualTo(replaced.Runtime.Id);
            await using var foreign = new NoCtfDbContext(fixture.Options);
            var stoppedForeign = await Store(fixture, foreign, Guid.NewGuid(), fixture.Right.Id).MutateAsync(new(request with { ActorId = fixture.Right.Id },
                RuntimeAction.Stop, replaced.Runtime.Id), ct);
            await Assert.That(stoppedForeign.Failure).IsEqualTo(RuntimeMutationFailure.NotFound);
            await using var stopDb = new NoCtfDbContext(fixture.Options);
            var stopped = await Store(fixture, stopDb, Guid.NewGuid()).MutateAsync(new(request, RuntimeAction.Stop, replaced.Runtime.Id), ct);
            await Assert.That(stopped.Failure).IsNull();
            await Assert.That(stopped.Runtime!.State).IsEqualTo(RuntimeState.Stopping);
        });
    }

    [Test, Timeout(300_000)]
    public async Task Unopened_foreign_route_and_expired_clock_cannot_provision_or_expose_execution_resources(CancellationToken ct)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var fixture = await LiveSoloMatchPersistenceTests.Fixture.CreateAsync(ct);
            await ConfigureAsync(fixture, ct); var request = Request(fixture);
            var store = Store(fixture, fixture.Db, Guid.NewGuid());
            await Assert.That(await store.ReadAsync(request with { MatchId = Guid.NewGuid() }, ct)).IsNull();
            await Assert.That((await store.MutateAsync(new(request with { QuestionId = fixture.Round.Questions.Single(x => x.Position == 1).Id },
                RuntimeAction.Start, null), ct)).Failure).IsEqualTo(RuntimeMutationFailure.NotFound);
            fixture.Now = fixture.Round.StartedAt!.Value.AddSeconds(fixture.Round.LimitSeconds);
            await Assert.That((await Store(fixture, fixture.Db, Guid.NewGuid()).MutateAsync(new(request, RuntimeAction.Start, null), ct)).Failure)
                .IsEqualTo(RuntimeMutationFailure.NotFound);
            await Assert.That(await fixture.Db.RuntimeInstances.CountAsync(ct)).IsEqualTo(0);
        });
    }

    [Test, Timeout(300_000)]
    public async Task Competing_replacements_for_one_expected_uuid_create_only_one_new_environment(CancellationToken ct)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var fixture = await LiveSoloMatchPersistenceTests.Fixture.CreateAsync(ct);
            await ConfigureAsync(fixture, ct); var request = Request(fixture);
            var original = await Store(fixture, fixture.Db, Guid.NewGuid()).MutateAsync(new(request, RuntimeAction.Start, null), ct);
            var command = new LiveSoloRuntimeCommand(request, RuntimeAction.Reset, original.Runtime!.Id);
            var results = await Task.WhenAll(Enumerable.Range(0, 2).Select(async _ =>
            {
                await using var db = new NoCtfDbContext(fixture.Options);
                return await Store(fixture, db, Guid.NewGuid()).MutateAsync(command, ct);
            }));
            await Assert.That(results.Count(x => x.Failure is null)).IsEqualTo(1);
            await Assert.That(results.Count(x => x.Failure == RuntimeMutationFailure.Conflict)).IsEqualTo(1);
            await using var read = new NoCtfDbContext(fixture.Options);
            await Assert.That(await read.RuntimeInstances.CountAsync(ct)).IsEqualTo(2);
            await Assert.That(await read.RuntimeInstances.CountAsync(x => x.ActiveSlot != null, ct)).IsEqualTo(1);
        });
    }

    private static async Task ConfigureAsync(LiveSoloMatchPersistenceTests.Fixture fixture, CancellationToken ct)
    {
        await fixture.PrepareAsync(ct); await fixture.StartAsync(ct);
        // This fixture tests relational authorization/control, while real provider isolation has separate Docker tests.
        fixture.Templates[0].Definition!.Runtime = new ContainerChallengeRuntimeTemplate { Allocation = PersistedRuntimeAllocation.PerTeam,
            FlagSource = PersistedRuntimeFlagSource.Static, Services = [new() { Name = "web", Image = "example/web:test" }] };
        await fixture.Db.SaveChangesAsync(ct);
    }
    private static LiveSoloResourceRequest Request(LiveSoloMatchPersistenceTests.Fixture fixture) => new(fixture.Competition.Id, fixture.Match.Id,
        fixture.Round.Id, fixture.Round.Questions.Single(x => x.Position == 0).Id, fixture.Left.Id, fixture.Now);
    private static LiveSoloRuntimeStore Store(LiveSoloMatchPersistenceTests.Fixture fixture, NoCtfDbContext db, Guid key, Guid? actor = null)
    {
        var access = new LiveSoloExecutionAccess(db);
        var isolation = Substitute.For<IExecutionRuntimeIsolation>();
        isolation.AssessAsync(Arg.Any<ExecutionIsolationRequest>(), Arg.Any<CancellationToken>()).Returns(new ExecutionIsolationAssessment(ExecutionIsolationStatus.Verified));
        var placement = Substitute.For<IRuntimePlacementPolicy>(); placement.Resolve(RuntimeKind.Container).Returns(new RuntimePlacement(RuntimeProvider.Docker, "test"));
        var control = new ScopedRuntimeControl(db, new ChallengeRuntimeTemplateCatalog(), placement, Substitute.For<IPerTeamRuntimeFlagStore>(),
            Substitute.For<IPostCommitMessagePublisher>(), isolation, access, fixture.Clock,
            new TransactionalRequestReplay(db, new CommandKey(key, actor ?? fixture.Left.Id), fixture.Clock));
        return new(db, access, control, fixture.Clock, isolation);
    }
    private sealed record CommandKey(Guid? Key, Guid? ActorId) : IRequestCommandKey;
}
