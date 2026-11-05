using Microsoft.EntityFrameworkCore;
using NoCTF.API.Endpoints.Administration.ChallengeBank;
using NoCTF.Application.Challenges.Bank;
using NoCTF.Domain.Challenges;
using NoCTF.Domain.Challenges.Capabilities;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Gameplay;
using NoCTF.Domain.Identity;
using NoCTF.Domain.Runtime;
using NoCTF.Domain.Teams;
using NoCTF.Infrastructure.Challenges.Bank;
using NoCTF.Infrastructure.Persistence;
using NoCTF.Tests.Unit.Gameplay;
using Testcontainers.PostgreSql;

namespace NoCTF.Tests.Integration.Persistence;

[Category("Integration")]
public sealed class ChallengeCapabilityUsageTests
{
    [Test, Timeout(300_000)]
    public async Task Ctf_reference_and_queued_environment_allow_removing_unused_check_but_keep_break(CancellationToken ct)
    {
        await using var fixture = await Fixture.CreateAsync(GameMode.Ctf, ct);
        fixture.Db.RuntimeInstances.Add(fixture.Runtime(RuntimePurpose.Player));
        fixture.Db.GameplayFacts.Add(new BreakAttemptGameplayFact { Id = Guid.NewGuid(), CompetitionId = fixture.CompetitionId,
            CompetitionChallengeId = fixture.EntryId, TeamId = fixture.TeamId, ActorUserId = fixture.OwnerId,
            State = GameplayFactState.Completed, Result = GameplayFactResult.Correct, OccurredAt = fixture.Now, UpdatedAt = fixture.Now });
        await fixture.Db.SaveChangesAsync(ct);
        var definition = await fixture.DefinitionAsync(ct);
        definition.Capabilities.RemoveAll(value => value.Kind == ChallengeCapabilityKind.Check);
        await Assert.That((await fixture.SaveAsync(definition, ct)).State).IsEqualTo(ChallengeTemplateWriteState.Succeeded);
        var persisted = await fixture.DefinitionAsync(ct);
        await Assert.That(persisted.Check).IsNull();
        persisted.Capabilities.RemoveAll(value => value.Kind == ChallengeCapabilityKind.Break);
        await Assert.That((await fixture.SaveAsync(persisted, ct)).State).IsEqualTo(ChallengeTemplateWriteState.ReferencedCapabilityConflict);
    }

    [Test, Timeout(300_000)]
    public async Task Attack_environment_allows_checker_edit_and_verification_environment_blocks_it(CancellationToken ct)
    {
        await using var fixture = await Fixture.CreateAsync(GameMode.Awdp, ct);
        fixture.Db.RuntimeInstances.Add(fixture.Runtime(RuntimePurpose.AttackTarget));
        await fixture.Db.SaveChangesAsync(ct);
        var definition = await fixture.DefinitionAsync(ct);
        definition.Check!.TimeoutSeconds = 45;
        await Assert.That((await fixture.SaveAsync(definition, ct)).State).IsEqualTo(ChallengeTemplateWriteState.Succeeded);
        fixture.Db.RuntimeInstances.Add(fixture.Runtime(RuntimePurpose.FixTarget));
        await fixture.Db.SaveChangesAsync(ct);
        definition = await fixture.DefinitionAsync(ct); definition.Check!.TimeoutSeconds = 60;
        await Assert.That((await fixture.SaveAsync(definition, ct)).State).IsEqualTo(ChallengeTemplateWriteState.ActiveRuntimeDefinitionConflict);
        fixture.Db.ChangeTracker.Clear();
        definition = await fixture.DefinitionAsync(ct);
        definition.Capabilities.RemoveAll(value => value.Kind == ChallengeCapabilityKind.Check);
        await Assert.That((await fixture.SaveAsync(definition, ct)).State).IsEqualTo(ChallengeTemplateWriteState.ReferencedCapabilityConflict);
    }

    [Test, Timeout(300_000)]
    public async Task Pending_break_protects_break_and_runtime_configuration_and_allows_unrelated_checker_edit(CancellationToken ct)
    {
        await using var fixture = await Fixture.CreateAsync(GameMode.Ctf, ct);
        fixture.Db.GameplayFacts.Add(new BreakAttemptGameplayFact { Id = Guid.NewGuid(), CompetitionId = fixture.CompetitionId,
            CompetitionChallengeId = fixture.EntryId, TeamId = fixture.TeamId, ActorUserId = fixture.OwnerId,
            State = GameplayFactState.Queued, OccurredAt = fixture.Now, UpdatedAt = fixture.Now });
        await fixture.Db.SaveChangesAsync(ct);
        var definition = await fixture.DefinitionAsync(ct); definition.Check!.TimeoutSeconds = 45;
        await Assert.That((await fixture.SaveAsync(definition, ct)).State).IsEqualTo(ChallengeTemplateWriteState.Succeeded);
        definition = await fixture.DefinitionAsync(ct); definition.Break!.HasFlagTemplate = true; definition.Break.FlagTemplate.Header = "protected";
        await Assert.That((await fixture.SaveAsync(definition, ct)).State).IsEqualTo(ChallengeTemplateWriteState.PendingExecutionConflict);
        fixture.Db.ChangeTracker.Clear();
        definition = await fixture.DefinitionAsync(ct); ((ContainerChallengeRuntimeTemplate)definition.Runtime!).Services[0].Image = "different-image";
        await Assert.That((await fixture.SaveAsync(definition, ct)).State).IsEqualTo(ChallengeTemplateWriteState.PendingExecutionConflict);
    }

    private sealed class Fixture(PostgreSqlContainer postgres, NoCtfDbContext db) : IAsyncDisposable
    {
        public NoCtfDbContext Db { get; } = db;
        public Guid OwnerId { get; } = Guid.NewGuid();
        public Guid CompetitionId { get; } = Guid.NewGuid();
        public Guid EntryId { get; } = Guid.NewGuid();
        public Guid ChallengeId { get; } = Guid.NewGuid();
        public Guid TeamId { get; } = Guid.NewGuid();
        public DateTimeOffset Now { get; } = DateTimeOffset.UtcNow;
        public static async Task<Fixture> CreateAsync(GameMode mode, CancellationToken ct)
        {
            var postgres = new PostgreSqlBuilder("postgres:17.10-alpine3.24@sha256:742f40ea20b9ff2ff31db5458d127452988a2164df9e17441e191f3b72252193").Build();
            await postgres.StartAsync(ct);
            var fixture = new Fixture(postgres, new(new DbContextOptionsBuilder<NoCtfDbContext>().UseNpgsql(postgres.GetConnectionString()).Options));
            await fixture.Db.Database.EnsureCreatedAsync(ct);
            fixture.Db.Users.Add(new() { Id = fixture.OwnerId, UserName = "author", NormalizedUserName = "AUTHOR", Role = UserRole.Organizer,
                Email = "author@example.test", NormalizedEmail = "AUTHOR@EXAMPLE.TEST" });
            Competition competition = mode switch
            {
                GameMode.Ctf => new CtfCompetition { ModeConfiguration = new CtfCompetitionModeConfiguration() },
                GameMode.Awdp => new AwdpCompetition { ModeConfiguration = new AwdpCompetitionModeConfiguration() },
                _ => throw new ArgumentOutOfRangeException(nameof(mode))
            };
            competition.Id = fixture.CompetitionId; competition.OwnerId = fixture.OwnerId; competition.Title = "Capabilities";
            fixture.Db.Competitions.Add(competition);
            fixture.Db.Challenges.Add(new() { Id = fixture.ChallengeId, OwnerId = fixture.OwnerId, Title = "Reusable", Direction = "Web",
                Definition = ChallengeCapabilityTests.Repairable() });
            CompetitionChallenge entry = mode == GameMode.Ctf ? new CtfCompetitionChallenge { Rules = new CtfCompetitionChallengeRules() }
                : new AwdpCompetitionChallenge { Rules = new AwdpCompetitionChallengeRules() };
            entry.Id = fixture.EntryId; entry.CompetitionId = fixture.CompetitionId; entry.ChallengeId = fixture.ChallengeId;
            fixture.Db.CompetitionChallenges.Add(entry);
            fixture.Db.Teams.Add(new() { Id = fixture.TeamId, CompetitionId = fixture.CompetitionId, CaptainId = fixture.OwnerId, MemberIds = [fixture.OwnerId],
                Name = "Team", NormalizedName = "TEAM", InvitationToken = new string('q', 32), RegistrationStatus = TeamRegistrationStatus.Approved });
            await fixture.Db.SaveChangesAsync(ct); return fixture;
        }
        public RuntimeInstance Runtime(RuntimePurpose purpose)
        {
            RuntimeInstance runtime = purpose switch
            {
                RuntimePurpose.Player => new PlayerRuntimeInstance(), RuntimePurpose.AttackTarget => new AttackTargetRuntimeInstance(),
                RuntimePurpose.FixTarget => new FixTargetRuntimeInstance(), _ => throw new ArgumentOutOfRangeException(nameof(purpose))
            };
            runtime.Id = Guid.NewGuid(); runtime.CompetitionId = CompetitionId; runtime.CompetitionChallengeId = EntryId; runtime.TeamId = TeamId;
            runtime.State = RuntimeState.Queued; runtime.RuntimeProvider = RuntimeProvider.Docker; runtime.RuntimeKind = RuntimeKind.Container; runtime.CreatedAt = Now;
            return runtime;
        }
        public async Task<ChallengeDefinition> DefinitionAsync(CancellationToken ct)
        {
            var loaded = await Db.Challenges.AsNoTracking().AsSplitQuery().SingleAsync(value => value.Id == ChallengeId, ct);
            return ChallengeDefinitionContractMapper.ToDomain(ChallengeId, ChallengeDefinitionContractMapper.FromDomain(loaded.Definition!));
        }
        public Task<ChallengeTemplateWriteResult> SaveAsync(ChallengeDefinition definition, CancellationToken ct) => new ChallengeBankStore(Db)
            .UpdateAsync(new(ChallengeId, OwnerId, false, ChallengeVisibility.Private, "Reusable", null, "Web", definition, DateTimeOffset.UtcNow), ct);
        public async ValueTask DisposeAsync() { await Db.DisposeAsync(); await postgres.DisposeAsync(); }
    }
}
