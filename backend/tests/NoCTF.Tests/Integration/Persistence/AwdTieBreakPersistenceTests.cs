using Microsoft.EntityFrameworkCore;
using NoCTF.API.Endpoints.Administration.Competitions;
using NoCTF.Application.Competitions.Awd;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Competitions.Awd;
using NoCTF.GameModes.Awd.Configuration;
using NoCTF.Infrastructure.Competitions.Awd;
using NoCTF.Infrastructure.Competitions.Configuration;
using NoCTF.Infrastructure.Competitions.Events;
using NoCTF.Infrastructure.Persistence;
using Testcontainers.PostgreSql;

namespace NoCTF.Tests.Integration.Persistence;

public sealed partial class AwdRoundCoordinationTests
{
    [Test, Timeout(300_000)]
    public async Task Ordered_tie_break_defaults_are_audited_and_only_enter_the_next_round(CancellationToken ct)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var postgres = new PostgreSqlBuilder("postgres:17.10-alpine3.24@sha256:742f40ea20b9ff2ff31db5458d127452988a2164df9e17441e191f3b72252193")
                .WithDatabase("awd_ranking").WithUsername("postgres").WithPassword("postgres").Build();
            await postgres.StartAsync(ct);
            var options = new DbContextOptionsBuilder<NoCtfDbContext>().UseNpgsql(postgres.GetConnectionString())
                .UseSnakeCaseNamingConvention().Options;
            var fixture = await SeedAsync(options, ct);
            await using var db = new NoCtfDbContext(options);
            var clock = new MutableTimeProvider(fixture.Now.AddSeconds(1));
            var outbox = new RecordingOutbox();
            var coordinator = new AwdRoundCoordinator(db, new AwdRoundConfigurationCatalog(), outbox, clock, Secrets());
            await coordinator.AdvanceAsync(new(fixture.CompetitionId, clock.UtcNow), ct);
            var round = await db.AwdRounds.AsNoTracking().SingleAsync(ct);
            await Assert.That(round.TieBreakers.Count).IsEqualTo(5);
            var store = new CompetitionConfigurationStore(db, outbox, new CompetitionEventStore(db, outbox));
            var configuration = (await store.FindAsync(fixture.CompetitionId, ct))!.Configuration;
            var contract = CompetitionModeConfigurationContractMapper.FromDomain(configuration);
            contract.Awd = contract.Awd! with { TieBreakers = [new(AwdTieBreakField.AttackCount, AwdSortDirection.Ascending),
                new(AwdTieBreakField.HealthyRounds, AwdSortDirection.Descending)] };
            var requested = CompetitionModeConfigurationContractMapper.ToDomain(fixture.CompetitionId, GameMode.Awd, contract);
            var audit = new AwdConfigurationChangeAudit(fixture.OwnerId, "Publish next-round tie-break order");
            var changed = await store.TryUpdateAsync(fixture.CompetitionId, requested, true, clock.UtcNow.AddSeconds(10), ct, audit);
            await Assert.That(changed.Failure).IsNull();
            db.ChangeTracker.Clear();
            var saved = (AwdCompetitionModeConfiguration)(await store.FindAsync(fixture.CompetitionId, ct))!.Configuration;
            await Assert.That(saved.TieBreakers.OrderBy(item => item.Position).First().Field).IsEqualTo(AwdTieBreakField.AttackCount);
            var published = (await new AwdPublicRulesReader(db).ReadAsync(fixture.CompetitionId, fixture.CompetitionChallengeId, clock.UtcNow, ct))!;
            await Assert.That(published.CurrentTieBreakers!.First().Field).IsEqualTo(AwdTieBreakField.AttackPoints);
            await Assert.That(published.NextTieBreakers!.First().Field).IsEqualTo(AwdTieBreakField.AttackCount);
            clock.UtcNow = fixture.Now.AddSeconds(301);
            await coordinator.AdvanceAsync(new(fixture.CompetitionId, clock.UtcNow), ct);
            db.ChangeTracker.Clear();
            var rounds = await db.AwdRounds.OrderBy(item => item.Number).ToArrayAsync(ct);
            await Assert.That(rounds[0].TieBreakers.Count).IsEqualTo(5);
            await Assert.That(rounds[1].TieBreakers.Count).IsEqualTo(2);
            await Assert.That(rounds[1].TieBreakers.OrderBy(item => item.Position).First().Direction).IsEqualTo(AwdSortDirection.Ascending);
            var auditEvent = await db.CompetitionEvents.SingleAsync(item => item.Reason == audit.Reason, ct);
            await Assert.That(auditEvent.AwdEffectiveFromRound).IsEqualTo(2);
            // Reordering two existing fields must release both unique constraints atomically.
            contract.Awd = contract.Awd with { TieBreakers = contract.Awd.TieBreakers!.Reverse().ToArray() };
            requested = CompetitionModeConfigurationContractMapper.ToDomain(fixture.CompetitionId, GameMode.Awd, contract);
            await Assert.That((await store.TryUpdateAsync(fixture.CompetitionId, requested, true, clock.UtcNow, ct, audit)).Failure).IsNull();
            db.ChangeTracker.Clear();
            saved = (AwdCompetitionModeConfiguration)(await store.FindAsync(fixture.CompetitionId, ct))!.Configuration;
            await Assert.That(saved.TieBreakers.OrderBy(item => item.Position).First().Field).IsEqualTo(AwdTieBreakField.HealthyRounds);
            await using (var duplicate = new NoCtfDbContext(options))
            {
                duplicate.Set<AwdRoundTieBreak>().Add(new() { RoundId = rounds[1].Id, Position = 99, Field = AwdTieBreakField.AttackCount });
                await Assert.That(async () => await duplicate.SaveChangesAsync(ct)).Throws<DbUpdateException>();
            }
            var immutable = await db.AwdRounds.SingleAsync(item => item.Number == 1, ct);
            immutable.TieBreakers[0].Direction = AwdSortDirection.Ascending;
            await Assert.That(async () => await db.SaveChangesAsync(ct)).Throws<InvalidOperationException>();
        });
    }
}
