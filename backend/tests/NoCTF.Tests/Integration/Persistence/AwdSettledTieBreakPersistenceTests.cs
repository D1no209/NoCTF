using Microsoft.EntityFrameworkCore;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Competitions.Awd;
using NoCTF.Domain.Competitions.Events;
using NoCTF.Domain.Gameplay;
using NoCTF.Domain.Teams;
using NoCTF.Infrastructure.Competitions.Awd;
using NoCTF.Infrastructure.Persistence;
using Testcontainers.PostgreSql;

namespace NoCTF.Tests.Integration.Persistence;

public sealed partial class LeaderboardProjectionPersistenceTests
{
    [Test, Timeout(300_000)]
    public Task Awd_target_rank_uses_the_last_settled_round_tie_break_policy(CancellationToken ct) => DockerIntegrationTest.RunAsync(async () =>
    {
        await using var postgres = new PostgreSqlBuilder("postgres:17.10-alpine3.24@sha256:742f40ea20b9ff2ff31db5458d127452988a2164df9e17441e191f3b72252193").Build();
        await postgres.StartAsync(ct);
        var options = new DbContextOptionsBuilder<NoCtfDbContext>().UseNpgsql(postgres.GetConnectionString()).UseSnakeCaseNamingConvention().Options;
        await using var db = new NoCtfDbContext(options); await db.Database.EnsureCreatedAsync(ct);
        var start = DateTimeOffset.UtcNow; var owner = CreateUser(start);
        var fixture = CreateFixture(GameMode.Awd, 0, owner.Id, start);
        fixture.Competition.StartAt = start; fixture.Competition.EndAt = start.AddSeconds(360);
        fixture.Team.RegisteredAt = start.AddMinutes(-2);
        ((AwdCompetitionModeConfiguration)fixture.Competition.ModeConfiguration!).TieBreakers =
            [new() { CompetitionId = fixture.Competition.Id, Position = 0, Field = AwdTieBreakField.RegistrationTime, Direction = AwdSortDirection.Ascending }];
        var later = CreateUser(start.AddSeconds(1)); later.UserName = later.NormalizedUserName = "later-ranked"; later.Email = "later-ranked@example.test";
        var team = new Team { Id = Guid.NewGuid(), CompetitionId = fixture.Competition.Id, Name = "Later",
            CaptainId = later.Id, MemberIds = [later.Id], InvitationToken = new string('t', 32),
            RegistrationStatus = TeamRegistrationStatus.Approved, RegisteredAt = start.AddMinutes(-1) };
        db.Users.AddRange(owner, later); db.Competitions.Add(fixture.Competition); db.Challenges.Add(fixture.Challenge);
        db.CompetitionChallenges.Add(fixture.CompetitionChallenge); db.Teams.AddRange(fixture.Team, team);
        db.CompetitionEvents.Add(new CompetitionLifecycleChangedEvent { Id = Guid.NewGuid(), CompetitionId = fixture.Competition.Id,
            PreviousCompetitionStatus = CompetitionStatus.Published, CompetitionStatus = CompetitionStatus.Running, OccurredAt = start });
        var round = new AwdRound { Id = Guid.NewGuid(), CompetitionId = fixture.Competition.Id, Number = 1,
            StartsAt = start, EndsAt = start.AddSeconds(180), ActiveEndTicks = TimeSpan.FromSeconds(180).Ticks,
            TieBreakers = [new() { Position = 0, Field = AwdTieBreakField.RegistrationTime, Direction = AwdSortDirection.Descending }] };
        var rule = new AwdRoundRules { Id = Guid.NewGuid(), CompetitionId = fixture.Competition.Id,
            CompetitionChallengeId = fixture.CompetitionChallenge.Id, RoundId = round.Id, Revision = 1, EffectiveAt = start,
            AttackRewardMode = AwdAttackRewardMode.FixedPerAttack, CheckerIntervalSeconds = 30, EligibleTeamCount = 2,
            Stores = [new() { StoreId = Guid.NewGuid(), Weight = 1, FlagsPerIssue = 1 }],
            Teams = [new() { TeamId = fixture.Team.Id }, new() { TeamId = team.Id }] };
        db.AwdRounds.Add(round); db.AwdRoundRules.Add(rule);
        foreach (var teamId in new[] { fixture.Team.Id, team.Id })
        {
            var id = Guid.NewGuid();
            db.GameplayFacts.Add(new AwdServiceTransitionGameplayFact { Id = id, CompetitionId = fixture.Competition.Id,
                CompetitionChallengeId = fixture.CompetitionChallenge.Id, TeamId = teamId, ReferenceKind = GameplayFactReferenceKind.AwdRound,
                ReferenceId = round.Id, OccurredAt = start.AddSeconds(150), UpdatedAt = start.AddSeconds(155), State = GameplayFactState.Completed,
                Result = GameplayFactResult.ServiceUp, Awd = new() { GameplayFactId = id, RulesId = rule.Id,
                    SampleActiveTicks = TimeSpan.FromSeconds(150).Ticks, SampleDeadlineActiveTicks = round.ActiveEndTicks,
                    FinalScheduledCheck = true, Health = AwdHealth.Ok, ManifestComplete = true, Settlement = AwdSettlementState.Final,
                    Execution = AwdCheckExecution.Completed, ObservedAt = start.AddSeconds(154), ReceivedAt = start.AddSeconds(155) } });
        }
        await db.SaveChangesAsync(ct);
        var ranks = await AwdSettledRankingReader.ReadAsync(db, fixture.Competition, 2,
            [fixture.Team.Id, team.Id], start.AddSeconds(181), ct);
        await Assert.That(ranks[team.Id]).IsEqualTo(1);
        await Assert.That(ranks[fixture.Team.Id]).IsEqualTo(2);
    });
}
