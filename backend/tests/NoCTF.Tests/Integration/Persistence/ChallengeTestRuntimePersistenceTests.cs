using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using NoCTF.Application.Challenges.Testing;
using NoCTF.Application.Runtime.Instances;
using NoCTF.Domain.Challenges;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Runtime;
using NoCTF.GameModes.Registration;
using NoCTF.Infrastructure.Challenges.Testing;
using NoCTF.Infrastructure.Runtime.Placement;

namespace NoCTF.Tests.Integration.Persistence;

[Category("Integration")]
public sealed class ChallengeTestRuntimePersistenceTests
{
    [Test, Timeout(300_000)]
    public async Task Template_commands_preserve_scope_and_reset_the_uuid_and_flag_atomically(CancellationToken ct)
    {
        await using var fixture = await ChallengeApplicationFixture.CreateAsync(CompetitionStatus.Draft, ct);
        var template = await fixture.Db.Challenges.AsSplitQuery().SingleAsync(value => value.Id == fixture.TemplateId, ct);
        template.Definition!.Runtime = new ContainerChallengeRuntimeTemplate { Services = [new() { Name = "web", Image = RuntimeExecutionFixture.Image }] };
        template.Definition.Break!.HasFlagTemplate = true;
        template.Definition.Break.FlagTemplate.Header = "test";
        template.Definition.Break.EnvironmentTargets.Add(new() { ServiceName = "web", VariableName = "CHALLENGE_FLAG" });
        await fixture.Db.SaveChangesAsync(ct);
        var store = Store(fixture);
        var command = new ChallengeTestRuntimeCommand(fixture.TemplateId, fixture.OwnerId, false, RuntimeAction.Start, null, fixture.Now);
        var started = await store.MutateAsync(command, ct);
        await Assert.That(started.Failure).IsNull();
        await Assert.That(started.Runtime!.State).IsEqualTo(RuntimeState.Queued);
        await Assert.That(started.Runtime.FlagDelivery).IsEqualTo(RuntimeTestFlagDelivery.Environment);
        var firstId = started.Runtime.Id;
        await Assert.That((await store.MutateAsync(command, ct)).Runtime!.Id).IsEqualTo(firstId);
        await Assert.That(await store.FindAsync(fixture.TemplateId, Guid.NewGuid(), false, ct)).IsNull();
        var reset = await store.MutateAsync(command with { Action = RuntimeAction.Reset, Now = fixture.Now.AddSeconds(1) }, ct);
        await Assert.That(reset.Runtime!.Id == firstId).IsFalse();
        fixture.Db.ChangeTracker.Clear();
        var runtimes = await fixture.Db.RuntimeInstances.AsSplitQuery().OrderBy(value => value.CreatedAt).ToArrayAsync(ct);
        await Assert.That(runtimes.Length).IsEqualTo(2);
        await Assert.That(runtimes.All(value => value.Purpose == RuntimePurpose.TemplateTest && value.ChallengeId == fixture.TemplateId
            && value.CompetitionId is null && value.CompetitionChallengeId is null)).IsTrue();
        await Assert.That(runtimes[0].State).IsEqualTo(RuntimeState.Stopped);
        await Assert.That(runtimes[0].TestFlagState).IsEqualTo(RuntimeTestFlagState.Canceled);
        var flags = await fixture.Db.ChallengeFlags.OrderBy(value => value.CreatedAt).ToArrayAsync(ct);
        await Assert.That(flags.Length).IsEqualTo(2);
        await Assert.That(flags[0].ValidUntil).IsNotNull();
        await Assert.That(flags[0].SpecificationId).IsEqualTo(firstId);
        await Assert.That(flags[1].SpecificationId).IsEqualTo(reset.Runtime.Id);
        await Assert.That(flags[0].Flag == flags[1].Flag).IsFalse();
        await Assert.That((await store.MutateAsync(command with { Action = RuntimeAction.Stop, Now = fixture.Now.AddSeconds(2) }, ct)).Runtime!.State)
            .IsEqualTo(RuntimeState.Stopped);
        await Assert.That((await store.MutateAsync(command with { ActorUserId = Guid.NewGuid() }, ct)).Failure).IsEqualTo(RuntimeMutationFailure.NotFound);
    }

    [Test, Timeout(300_000)]
    public async Task Renewal_enforces_the_final_ten_minute_window_and_keeps_the_runtime_identity(CancellationToken ct)
    {
        await using var fixture = await ChallengeApplicationFixture.CreateAsync(CompetitionStatus.Draft, ct);
        var runtime = new TemplateTestRuntimeInstance { Id = Guid.NewGuid(), ChallengeId = fixture.TemplateId,
            RuntimeKind = RuntimeKind.Container, RuntimeProvider = RuntimeProvider.Docker, State = RuntimeState.Running,
            TestFlagDelivery = RuntimeTestFlagDelivery.NotRequired, TestFlagState = RuntimeTestFlagState.NotRequired,
            CreatedAt = fixture.Now, RunningAt = fixture.Now, ExpiresAt = fixture.Now.AddMinutes(52) };
        fixture.Db.RuntimeInstances.Add(runtime); await fixture.Db.SaveChangesAsync(ct);
        var store = Store(fixture);
        var command = new ChallengeTestRuntimeCommand(fixture.TemplateId, fixture.OwnerId, false, RuntimeAction.Extend, TimeSpan.FromMinutes(30), fixture.Now);
        await Assert.That((await store.MutateAsync(command, ct)).Failure).IsEqualTo(RuntimeMutationFailure.ExtensionTooEarly);
        fixture.Db.ChangeTracker.Clear();
        var extended = await store.MutateAsync(command with { Now = fixture.Now.AddMinutes(43) }, ct);
        await Assert.That(extended.Runtime!.ExpiresAt).IsEqualTo(fixture.Now.AddMinutes(82));
        await Assert.That(extended.Runtime.Id).IsEqualTo(runtime.Id);
        await Assert.That(await fixture.Db.RuntimeInstances.CountAsync(ct)).IsEqualTo(1);
    }

    private static ChallengeTestRuntimeStore Store(ChallengeApplicationFixture fixture) => new(fixture.Db, new ChallengeRuntimeTemplateCatalog(),
        new ConfiguredRuntimePlacementPolicy(Options.Create(new RuntimePlacementOptions { Provider = RuntimeProvider.Docker, RunnerPool = RuntimeExecutionFixture.Pool })),
        fixture.Publisher);
}
