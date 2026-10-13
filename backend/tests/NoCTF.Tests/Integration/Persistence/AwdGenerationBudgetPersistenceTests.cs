using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Messaging;
using NoCTF.Application.Scoring.Leaderboard;
using NoCTF.Domain.Challenges;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Competitions.Awd;
using NoCTF.Domain.Identity;
using NoCTF.Domain.Teams;
using NoCTF.GameModes.Awd.Configuration;
using NoCTF.GameModes.Awd.Scoring;
using NoCTF.Infrastructure.Competitions.Awd;
using NoCTF.Infrastructure.Persistence;
using Testcontainers.PostgreSql;

namespace NoCTF.Tests.Integration.Persistence;

public sealed partial class AwdRoundCoordinationTests
{
    public enum IssuanceEdit { FlagCount, AddStore, StoreWeight }

    [Test, Timeout(300_000)]
    [Arguments(IssuanceEdit.FlagCount), Arguments(IssuanceEdit.AddStore), Arguments(IssuanceEdit.StoreWeight)]
    public Task Active_interval_budget_changes_issue_a_complete_new_epoch(IssuanceEdit edit, CancellationToken ct) => DockerIntegrationTest.RunAsync(async () =>
    {
        await using var postgres = new PostgreSqlBuilder("postgres:17.10-alpine3.24@sha256:742f40ea20b9ff2ff31db5458d127452988a2164df9e17441e191f3b72252193").Build();
        await postgres.StartAsync(ct);
        var options = new DbContextOptionsBuilder<NoCtfDbContext>().UseNpgsql(postgres.GetConnectionString()).UseSnakeCaseNamingConvention().Options;
        var fixture = await SeedAsync(options, ct);
        await using var db = new NoCtfDbContext(options);
        var playerId = Guid.CreateVersion7(); var attackerId = Guid.CreateVersion7();
        db.Users.Add(new User { Id = playerId, UserName = "epoch-attacker", NormalizedUserName = "EPOCH-ATTACKER",
            Email = "epoch-attacker@example.test", PasswordHash = "test", CreatedAt = fixture.Now, UpdatedAt = fixture.Now });
        db.Teams.Add(new Team { Id = attackerId, CompetitionId = fixture.CompetitionId, Name = "Attacker",
            CaptainId = playerId, MemberIds = [playerId], InvitationToken = new string('e', 32),
            RegistrationStatus = TeamRegistrationStatus.Approved, RegisteredAt = fixture.Now });
        var competition = await db.Competitions.SingleAsync(ct);
        var configuration = (AwdCompetitionModeConfiguration)competition.ModeConfiguration!;
        configuration.RoundDurationSeconds = 180;
        configuration.AwdOptions = configuration.AwdOptions with { GenerationPolicy = AwdGenerationPolicy.ActiveInterval, GenerationInterval = 400 };
        var template = await db.Challenges.SingleAsync(ct);
        var definition = (AwdChallengeDefinition)template.Definition!;
        definition.FlagStores.Add(new() { Id = Guid.CreateVersion7(), Position = 1, Name = "second", DeliveryMode = AwdFlagDeliveryMode.BusinessChecker });
        await db.SaveChangesAsync(ct);
        var clock = new MutableTimeProvider(fixture.Now); var outbox = new RecordingOutbox();
        var coordinator = new AwdRoundCoordinator(db, new AwdRoundConfigurationCatalog(), outbox, clock, Secrets());
        async Task Generate(int seconds)
        {
            clock.UtcNow = fixture.Now.AddSeconds(seconds); outbox.Published.Clear();
            await coordinator.AdvanceAsync(new(fixture.CompetitionId, clock.UtcNow), ct);
            var message = outbox.Published.OfType<GenerateAwdFlags>().Single();
            await coordinator.GenerateFlagsAsync(message, ct);
            await using var restarted = new NoCtfDbContext(options);
            await new AwdRoundCoordinator(restarted, new AwdRoundConfigurationCatalog(), new RecordingOutbox(), clock, Secrets()).GenerateFlagsAsync(message, ct);
        }
        await Generate(0);
        var oldValues = await db.ChallengeFlags.AsNoTracking().ToDictionaryAsync(flag => flag.Id, flag => flag.Flag, ct);
        if (edit == IssuanceEdit.FlagCount) configuration.AwdOptions = configuration.AwdOptions with { FlagsPerStore = 2 };
        else if (edit == IssuanceEdit.AddStore) definition.FlagStores.Add(new() { Id = Guid.CreateVersion7(), Position = 2, Name = "third", DeliveryMode = AwdFlagDeliveryMode.BusinessChecker });
        else
        {
            var challenge = await db.CompetitionChallenges.SingleAsync(ct);
            ((AwdCompetitionChallengeRules)challenge.Rules!).StoreRules.Add(new() { StoreId = definition.FlagStores[1].Id, Weight = 3m });
        }
        await db.SaveChangesAsync(ct);
        await Generate(180);
        var rules = await db.AwdRoundRules.AsNoTracking().OrderBy(rule => rule.EffectiveAt).ToArrayAsync(ct);
        await Assert.That(rules.Length).IsEqualTo(2);
        await Assert.That(rules[1].GenerationEpochId).IsNotEqualTo(rules[0].GenerationEpochId);
        var issues = await db.AwdFlagIssues.AsNoTracking().Where(issue => issue.TeamId == fixture.TeamId).ToArrayAsync(ct);
        var current = issues.Where(issue => issue.RulesId == rules[1].Id).ToArray();
        await Assert.That(current.Length).IsEqualTo(edit == IssuanceEdit.FlagCount ? 4 : edit == IssuanceEdit.AddStore ? 3 : 2);
        await Assert.That(current.All(issue => issue.GenerationSlot == 0)).IsTrue();
        var rounds = await db.AwdRounds.AsNoTracking().ToArrayAsync(ct);
        var full = AwdRoundScoreProjector.Project(rounds, rules, issues,
            issues.Select(issue => new AwdScoringCapture(issue.FlagId, attackerId, clock.UtcNow)).ToArray(), [],
            new HashSet<Guid> { fixture.TeamId, attackerId }, rounds.Max(round => round.ActiveStartTicks), 2, false);
        await Assert.That(full.Where(row => row.TeamId == attackerId).SelectMany(row => row.Components)
            .Where(component => component.Component == AwdScoreComponent.Attack).Sum(component => component.Points)).IsEqualTo(100L);
        var selected = current.First(issue => issue.StoreId == definition.FlagStores[0].Id);
        var partial = AwdRoundScoreProjector.Project(rounds, rules, issues,
            [new(selected.FlagId, attackerId, clock.UtcNow)], [], new HashSet<Guid> { fixture.TeamId, attackerId },
            rounds.Max(round => round.ActiveStartTicks), 2, false);
        await Assert.That(partial.Where(row => row.TeamId == attackerId).SelectMany(row => row.Components)
            .Where(component => component.Component == AwdScoreComponent.Attack).Sum(component => component.Points))
            .IsEqualTo(edit == IssuanceEdit.AddStore ? 16L : 12L);
        await Generate(360);
        await Assert.That(await db.ChallengeFlags.CountAsync(ct)).IsEqualTo((2 + current.Length) * 2);
        foreach (var flag in await db.ChallengeFlags.AsNoTracking().Where(flag => oldValues.Keys.Contains(flag.Id)).ToArrayAsync(ct))
            await Assert.That(flag.Flag).IsEqualTo(oldValues[flag.Id]);
    });
}
