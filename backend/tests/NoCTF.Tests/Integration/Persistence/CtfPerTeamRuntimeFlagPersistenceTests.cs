using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using NoCTF.Application.Runtime.Instances;
using NoCTF.Application.Runtime.Provisioning;
using NoCTF.Domain.Challenges;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Identity;
using NoCTF.Domain.Runtime;
using NoCTF.Domain.Teams;
using NoCTF.GameModes.Registration;
using NoCTF.Infrastructure.Challenges.Flags;
using NoCTF.Infrastructure.Competitions.Events;
using NoCTF.Infrastructure.Runtime.Instances;
using NoCTF.Infrastructure.Runtime.Placement;
using NoCTF.Worker.Runtime;

namespace NoCTF.Tests.Integration.Persistence;

[Category("Integration"), NotInParallel]
public sealed class CtfPerTeamRuntimeFlagPersistenceTests
{
    [Test, Timeout(300_000)]
    public async Task Accepted_start_and_reset_use_one_team_flag_and_distinct_real_runtime_uuids(CancellationToken ct)
    {
        await using var fixture = await RuntimeExecutionFixture.CreateAsync(ct);
        var scope = await SeedAsync(fixture, ct);
        var store = Store(fixture);
        var command = new RuntimeMutationCommand(fixture.Application.CompetitionId, scope.ChallengeId, fixture.Application.OwnerId, RuntimeAction.Start, null, DateTimeOffset.UtcNow);
        var started = await store.MutatePlayerRuntimeAsync(command, ct);
        await Assert.That(started.Failure).IsNull();
        var flag = await fixture.Db.ChallengeFlags.SingleAsync(value => value.CompetitionChallengeId == scope.ChallengeId, ct);
        var first = await ProvisionAsync(fixture, scope.ChallengeId, started.Runtime!.Id, flag.Flag, ct);
        var receipt = (ContainerRuntimeReceiptData)first.ProviderReceipt;
        var environment = await fixture.Providers.Runtime(RuntimeProvider.Docker).ExecAsync(receipt.ToReceipt(), "main",
            ["/bin/sh", "-c", "test \"$CHALLENGE_FLAG\" = \"$1\"", "check-flag", flag.Flag], TimeSpan.FromSeconds(5), ct);
        await Assert.That(environment.ExitCode).IsEqualTo(0);
        var reset = await store.MutatePlayerRuntimeAsync(command with { Action = RuntimeAction.Reset, Now = DateTimeOffset.UtcNow }, ct);
        await Assert.That(reset.Failure).IsNull();
        await Assert.That(reset.Runtime!.Id == started.Runtime.Id).IsFalse();
        await Assert.That(await fixture.Db.ChallengeFlags.CountAsync(value => value.CompetitionChallengeId == scope.ChallengeId, ct)).IsEqualTo(1);
        await Assert.That((await fixture.Db.ChallengeFlags.AsNoTracking().SingleAsync(value => value.Id == flag.Id, ct)).Flag).IsEqualTo(flag.Flag);
        var second = await ProvisionAsync(fixture, scope.ChallengeId, reset.Runtime.Id, flag.Flag, ct);
        await Assert.That(second.RuntimeInstanceId).IsEqualTo(reset.Runtime.Id);
        await Assert.That((await fixture.Db.RuntimeInstances.AsNoTracking().SingleAsync(value => value.Id == first.RuntimeInstanceId, ct)).State).IsEqualTo(RuntimeState.Stopping);
        var page = await store.ListTeamRuntimesAsync(fixture.Application.CompetitionId, fixture.Application.OwnerId, 0, 10, true, ct);
        await Assert.That(page!.Items.Select(value => value.Runtime.Id)).IsEquivalentTo(new[] { reset.Runtime.Id });
    }

    [Test, Timeout(300_000)]
    public async Task Current_runtime_lists_are_member_scoped_and_renewal_preserves_unspent_time(CancellationToken ct)
    {
        await using var fixture = await RuntimeExecutionFixture.CreateAsync(ct);
        var scope = await SeedAsync(fixture, ct);
        var store = Store(fixture); var now = DateTimeOffset.UtcNow;
        var command = new RuntimeMutationCommand(fixture.Application.CompetitionId, scope.ChallengeId, fixture.Application.OwnerId, RuntimeAction.Start, null, now);
        var started = await store.MutatePlayerRuntimeAsync(command, ct);
        await Assert.That(started.Failure).IsNull();
        var flag = await fixture.Db.ChallengeFlags.SingleAsync(value => value.CompetitionChallengeId == scope.ChallengeId, ct);
        await ProvisionAsync(fixture, scope.ChallengeId, started.Runtime!.Id, flag.Flag, ct);
        await Assert.That((await store.MutatePlayerRuntimeAsync(command with { Action = RuntimeAction.Extend, Extension = TimeSpan.FromMinutes(30), Now = now.AddMinutes(1) }, ct)).Failure)
            .IsEqualTo(RuntimeMutationFailure.ExtensionTooEarly);
        var runtime = await fixture.Db.RuntimeInstances.SingleAsync(value => value.Id == started.Runtime.Id, ct);
        var expires = runtime.ExpiresAt!.Value;
        var extended = await store.MutatePlayerRuntimeAsync(command with { Action = RuntimeAction.Extend, Extension = TimeSpan.FromMinutes(30), Now = expires.AddMinutes(-9) }, ct);
        await Assert.That(extended.Failure).IsNull();
        await Assert.That(extended.Runtime!.ExpiresAt).IsEqualTo(expires.AddMinutes(30));
        await Assert.That((await store.ListTeamRuntimesAsync(fixture.Application.CompetitionId, scope.OtherUserId, 0, 10, true, ct))!.Items).IsEmpty();
        await Assert.That(await store.ListTeamRuntimesAsync(fixture.Application.CompetitionId, Guid.NewGuid(), 0, 10, true, ct)).IsNull();
    }

    [Test, Timeout(300_000)]
    public async Task Batch_generation_retains_existing_flags_and_uses_new_rules_for_new_participants(CancellationToken ct)
    {
        await using var fixture = await RuntimeExecutionFixture.CreateAsync(ct);
        var scope = await SeedAsync(fixture, ct);
        var templates = new ChallengeRuntimeTemplateCatalog(); var flags = new PerTeamRuntimeFlagStore(fixture.Db);
        var generator = new MissingFlagGenerator(fixture.Db, templates, flags);
        await Assert.That(await generator.GenerateAsync(fixture.Application.CompetitionId, DateTimeOffset.UtcNow, ct)).IsEmpty();
        var existing = await fixture.Db.ChallengeFlags.SingleAsync(value => value.CompetitionChallengeId == scope.ChallengeId && value.TeamId == scope.TeamId, ct);
        var rules = (CtfCompetitionChallengeRules)(await fixture.Db.CompetitionChallenges.SingleAsync(value => value.Id == scope.ChallengeId, ct)).Rules!;
        rules.FlagTemplate.Header = "updated";
        var nextUser = new User { Id = Guid.NewGuid(), UserName = "next", NormalizedUserName = "NEXT", Email = "next@example.test", AccountStatus = UserAccountStatus.Active };
        var nextTeam = new Team { Id = Guid.NewGuid(), CompetitionId = fixture.Application.CompetitionId, CaptainId = nextUser.Id,
            MemberIds = [nextUser.Id], Name = "Next", InvitationToken = new string('n', 32), RegistrationStatus = TeamRegistrationStatus.Approved };
        fixture.Db.Users.Add(nextUser); fixture.Db.Teams.Add(nextTeam); await fixture.Db.SaveChangesAsync(ct);
        var retained = await flags.EnsureAsync(fixture.Application.CompetitionId, scope.ChallengeId, scope.TeamId, DateTimeOffset.UtcNow, ct);
        var generated = await flags.EnsureAsync(fixture.Application.CompetitionId, scope.ChallengeId, nextTeam.Id, DateTimeOffset.UtcNow, ct);
        await fixture.Db.SaveChangesAsync(ct);
        await Assert.That(retained).IsEqualTo(existing.Flag);
        await Assert.That(generated.StartsWith("updated{", StringComparison.Ordinal)).IsTrue();
    }

    private static async Task<RuntimeProvisioned> ProvisionAsync(RuntimeExecutionFixture fixture, Guid challengeId, Guid runtimeId, string flag, CancellationToken ct)
    {
        var runtime = await fixture.Db.RuntimeInstances.SingleAsync(value => value.Id == runtimeId, ct);
        var entry = await fixture.Db.CompetitionChallenges.AsSplitQuery().SingleAsync(value => value.Id == challengeId, ct);
        var definition = (await fixture.Db.Challenges.AsSplitQuery().SingleAsync(value => value.Id == entry.ChallengeId, ct)).Definition!;
        var template = new ChallengeRuntimeTemplateCatalog().Get(definition, entry.Rules!)!;
        var message = (ProvisionContainerRuntime)RuntimeClaimFactory.Create(runtime, RuntimeExecutionFixture.RunnerId, GameMode.Ctf, template, definition, flag);
        await fixture.ClaimAsync(message, ct);
        var provisioned = await fixture.Handler.ProvisionContainerAsync(message, ct) as RuntimeProvisioned
            ?? throw new InvalidOperationException("The team Runtime did not start.");
        await fixture.PersistAsync(provisioned, ct); return provisioned;
    }

    private static RuntimeInstanceStore Store(RuntimeExecutionFixture fixture) => new(fixture.Db, new ChallengeRuntimeTemplateCatalog(),
        new ConfiguredRuntimePlacementPolicy(Options.Create(new RuntimePlacementOptions { RunnerPool = RuntimeExecutionFixture.Pool })),
        new PerTeamRuntimeFlagStore(fixture.Db), fixture.Application.Publisher, new CompetitionEventStore(fixture.Db, fixture.Application.Publisher));

    private static async Task<Scope> SeedAsync(RuntimeExecutionFixture fixture, CancellationToken ct)
    {
        var db = fixture.Db; var competition = await db.Competitions.SingleAsync(value => value.Id == fixture.Application.CompetitionId, ct);
        competition.RuntimeAccessMode = RuntimeAccessMode.Direct; competition.FlagDerivationSecret = RandomNumberGenerator.GetBytes(32);
        var template = await db.Challenges.AsSplitQuery().SingleAsync(value => value.Id == fixture.Application.TemplateId, ct);
        template.Definition!.Runtime = new ContainerChallengeRuntimeTemplate { Services = [new() { Name = "main", Image = RuntimeExecutionFixture.Image,
            CpuCores = .1m, MemoryMiB = 64, Commands = [new() { Position = 0, Value = "sleep" }, new() { Position = 1, Value = "3600" }] }] };
        template.Definition.Break!.EnvironmentTargets.Add(new() { ServiceName = "main", VariableName = "CHALLENGE_FLAG" });
        var entryId = Guid.NewGuid(); var teamId = Guid.NewGuid(); var otherUserId = Guid.NewGuid();
        db.CompetitionChallenges.Add(new CtfCompetitionChallenge { Id = entryId, CompetitionId = competition.Id, ChallengeId = template.Id, IsPublished = true,
            Rules = new CtfCompetitionChallengeRules { HasFlagTemplate = true, FlagTemplate = new() { Header = "challenge", BodyTemplate = "[TEAMHASH]" },
                Runtime = new() { Allocation = PersistedRuntimeAllocation.PerTeam, FlagSource = PersistedRuntimeFlagSource.PerTeam, TtlSeconds = 3600 } } });
        db.Users.Add(new User { Id = otherUserId, UserName = "other", NormalizedUserName = "OTHER", Email = "other@example.test", AccountStatus = UserAccountStatus.Active });
        db.Teams.AddRange(new Team { Id = teamId, CompetitionId = competition.Id, CaptainId = fixture.Application.OwnerId, MemberIds = [fixture.Application.OwnerId],
            Name = "Owner team", InvitationToken = new string('o', 32), RegistrationStatus = TeamRegistrationStatus.Approved },
            new Team { Id = Guid.NewGuid(), CompetitionId = competition.Id, CaptainId = otherUserId, MemberIds = [otherUserId],
                Name = "Other team", InvitationToken = new string('t', 32), RegistrationStatus = TeamRegistrationStatus.Approved });
        await db.SaveChangesAsync(ct); return new(entryId, teamId, otherUserId);
    }
    private sealed record Scope(Guid ChallengeId, Guid TeamId, Guid OtherUserId);
}
