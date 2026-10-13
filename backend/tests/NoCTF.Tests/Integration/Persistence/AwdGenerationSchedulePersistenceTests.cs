using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Messaging;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Competitions.Awd;
using NoCTF.GameModes.Awd.Configuration;
using NoCTF.Infrastructure.Competitions.Awd;
using NoCTF.Infrastructure.Persistence;
using Testcontainers.PostgreSql;

namespace NoCTF.Tests.Integration.Persistence;

public sealed partial class AwdRoundCoordinationTests
{
    [Test, Timeout(300_000), Arguments(5), Arguments(7)]
    public Task Active_interval_cadence_uses_actual_issuance_time_without_backfill(int interval, CancellationToken ct) => DockerIntegrationTest.RunAsync(async () =>
    {
        await using var postgres = new PostgreSqlBuilder("postgres:17.10-alpine3.24@sha256:742f40ea20b9ff2ff31db5458d127452988a2164df9e17441e191f3b72252193").Build();
        await postgres.StartAsync(ct);
        var options = new DbContextOptionsBuilder<NoCtfDbContext>().UseNpgsql(postgres.GetConnectionString()).UseSnakeCaseNamingConvention().Options;
        var fixture = await SeedAsync(options, ct);
        await using var db = new NoCtfDbContext(options);
        var competition = await db.Competitions.SingleAsync(ct);
        var configuration = (AwdCompetitionModeConfiguration)competition.ModeConfiguration!;
        configuration.RoundDurationSeconds = 180;
        configuration.AwdOptions = configuration.AwdOptions with { GenerationPolicy = AwdGenerationPolicy.ActiveInterval,
            GenerationInterval = interval, FlagLifetimeUnit = AwdLifetimeUnit.ActiveSeconds, FlagLifetime = 100 };
        await db.SaveChangesAsync(ct);
        var clock = new MutableTimeProvider(fixture.Now); var outbox = new RecordingOutbox();
        var coordinator = new AwdRoundCoordinator(db, new AwdRoundConfigurationCatalog(), outbox, clock, Secrets());
        async Task Generate(int seconds)
        {
            clock.UtcNow = fixture.Now.AddSeconds(seconds); outbox.Published.Clear();
            await coordinator.AdvanceAsync(new(fixture.CompetitionId, clock.UtcNow), ct);
            var message = outbox.Published.OfType<GenerateAwdFlags>().Single();
            await coordinator.GenerateFlagsAsync(message, ct); await coordinator.GenerateFlagsAsync(message, ct);
        }
        for (var seconds = 0; seconds <= 40; seconds += AwdGenerationPlan.CoordinationIntervalSeconds)
        {
            await Generate(seconds);
            await Assert.That(await db.AwdFlagIssues.CountAsync(ct)).IsEqualTo(seconds / interval + 1);
        }
        var before = await db.AwdFlagIssues.CountAsync(ct);
        await Generate(90);
        await Assert.That(await db.AwdFlagIssues.CountAsync(ct)).IsEqualTo(before + 1);
        var issues = await db.AwdFlagIssues.AsNoTracking().OrderBy(issue => issue.GenerationSlot).ToArrayAsync(ct);
        var anchor = issues[0].ActiveStartTicks;
        foreach (var issue in issues)
        {
            var actualSeconds = (issue.IssuedAt - fixture.Now).TotalSeconds;
            await Assert.That(issue.ActiveStartTicks - anchor).IsEqualTo(TimeSpan.FromSeconds(actualSeconds).Ticks);
            await Assert.That(issue.ActiveExpiryTicks - issue.ActiveStartTicks).IsEqualTo(TimeSpan.FromSeconds(100).Ticks);
            await Assert.That(issue.GenerationSlot).IsEqualTo((long)actualSeconds / interval);
        }
    });

    [Test, Timeout(300_000)]
    [Arguments(AwdGenerationPolicy.ActiveInterval)]
    [Arguments(AwdGenerationPolicy.EveryNRounds)]
    public Task Generation_schedule_changes_and_reverting_parameters_start_distinct_epochs(
        AwdGenerationPolicy policy, CancellationToken ct) => DockerIntegrationTest.RunAsync(async () =>
    {
        await using var postgres = new PostgreSqlBuilder("postgres:17.10-alpine3.24@sha256:742f40ea20b9ff2ff31db5458d127452988a2164df9e17441e191f3b72252193").Build();
        await postgres.StartAsync(ct);
        var options = new DbContextOptionsBuilder<NoCtfDbContext>().UseNpgsql(postgres.GetConnectionString()).UseSnakeCaseNamingConvention().Options;
        var fixture = await SeedAsync(options, ct);
        await using var db = new NoCtfDbContext(options);
        var competition = await db.Competitions.SingleAsync(item => item.Id == fixture.CompetitionId, ct);
        var configuration = (AwdCompetitionModeConfiguration)competition.ModeConfiguration!;
        configuration.RoundDurationSeconds = 180;
        configuration.AwdOptions = configuration.AwdOptions with { GenerationPolicy = policy,
            GenerationInterval = policy == AwdGenerationPolicy.ActiveInterval ? 60 : 2 };
        await db.SaveChangesAsync(ct);
        var clock = new MutableTimeProvider(fixture.Now);
        var outbox = new RecordingOutbox();
        var coordinator = new AwdRoundCoordinator(db, new AwdRoundConfigurationCatalog(), outbox, clock, Secrets());
        async Task<Guid> Generate(int seconds, int expectedFlags)
        {
            clock.UtcNow = fixture.Now.AddSeconds(seconds);
            outbox.Published.Clear();
            await coordinator.AdvanceAsync(new(fixture.CompetitionId, clock.UtcNow), ct);
            var message = outbox.Published.OfType<GenerateAwdFlags>().Single();
            await coordinator.GenerateFlagsAsync(message, ct);
            await using var restarted = new NoCtfDbContext(options);
            await new AwdRoundCoordinator(restarted, new AwdRoundConfigurationCatalog(), new RecordingOutbox(), clock, Secrets())
                .GenerateFlagsAsync(message, ct);
            await Assert.That(await restarted.AwdFlagIssues.CountAsync(ct)).IsEqualTo(expectedFlags);
            return await restarted.AwdRoundRules.OrderByDescending(item => item.EffectiveAt)
                .Select(item => item.GenerationEpochId).FirstAsync(ct);
        }
        var first = await Generate(0, 1);
        if (policy == AwdGenerationPolicy.ActiveInterval)
        {
            await Generate(60, 2); await Generate(120, 3);
            configuration.AwdOptions = configuration.AwdOptions with { GenerationInterval = 180 };
            await db.SaveChangesAsync(ct);
            var second = await Generate(180, 4);
            await Assert.That(second).IsNotEqualTo(first);
            await Assert.That(await Generate(360, 5)).IsEqualTo(second);
            configuration.AwdOptions = configuration.AwdOptions with { GenerationInterval = 60 };
            await db.SaveChangesAsync(ct);
            var reverted = await Generate(540, 6);
            await Assert.That(reverted).IsNotEqualTo(first);
            await Assert.That(reverted).IsNotEqualTo(second);
        }
        else
        {
            await Assert.That(await Generate(180, 1)).IsEqualTo(first);
            configuration.AwdOptions = configuration.AwdOptions with { GenerationInterval = 3 };
            await db.SaveChangesAsync(ct);
            var second = await Generate(360, 2);
            await Assert.That(second).IsNotEqualTo(first);
            await Generate(540, 2); await Generate(720, 2); await Generate(900, 3);
            configuration.AwdOptions = configuration.AwdOptions with { GenerationInterval = 2 };
            await db.SaveChangesAsync(ct);
            var reverted = await Generate(1080, 4);
            await Assert.That(reverted).IsNotEqualTo(first);
            await Assert.That(reverted).IsNotEqualTo(second);
            await Generate(1260, 4); await Generate(1440, 5);
        }
    });

    [Test, Timeout(300_000)]
    public Task One_active_interval_spanning_rounds_keeps_its_epoch_and_does_not_reissue(CancellationToken ct) => DockerIntegrationTest.RunAsync(async () =>
    {
        await using var postgres = new PostgreSqlBuilder("postgres:17.10-alpine3.24@sha256:742f40ea20b9ff2ff31db5458d127452988a2164df9e17441e191f3b72252193").Build();
        await postgres.StartAsync(ct);
        var options = new DbContextOptionsBuilder<NoCtfDbContext>().UseNpgsql(postgres.GetConnectionString()).UseSnakeCaseNamingConvention().Options;
        var fixture = await SeedAsync(options, ct);
        await using var db = new NoCtfDbContext(options);
        var competition = await db.Competitions.SingleAsync(item => item.Id == fixture.CompetitionId, ct);
        var configuration = (AwdCompetitionModeConfiguration)competition.ModeConfiguration!;
        configuration.RoundDurationSeconds = 180;
        configuration.AwdOptions = configuration.AwdOptions with { GenerationPolicy = AwdGenerationPolicy.ActiveInterval, GenerationInterval = 400 };
        await db.SaveChangesAsync(ct);
        var clock = new MutableTimeProvider(fixture.Now);
        var outbox = new RecordingOutbox();
        var coordinator = new AwdRoundCoordinator(db, new AwdRoundConfigurationCatalog(), outbox, clock, Secrets());
        foreach (var seconds in new[] { 0, 180, 360, 400 })
        {
            clock.UtcNow = fixture.Now.AddSeconds(seconds);
            outbox.Published.Clear();
            await coordinator.AdvanceAsync(new(fixture.CompetitionId, clock.UtcNow), ct);
            var message = outbox.Published.OfType<GenerateAwdFlags>().Single();
            await coordinator.GenerateFlagsAsync(message, ct);
            await coordinator.GenerateFlagsAsync(message, ct);
            await Assert.That(await db.AwdFlagIssues.CountAsync(ct)).IsEqualTo(seconds < 400 ? 1 : 2);
            configuration.AttackPoints++;
            await db.SaveChangesAsync(ct);
        }
        await Assert.That(await db.AwdRoundRules.Select(item => item.GenerationEpochId).Distinct().CountAsync(ct)).IsEqualTo(1);
        await Assert.That(await db.AwdRoundRules.CountAsync(ct)).IsEqualTo(3);
    });
}
