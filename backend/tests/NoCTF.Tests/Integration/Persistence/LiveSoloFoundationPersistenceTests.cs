using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using NoCTF.Domain.Challenges;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Gameplay;
using NoCTF.Domain.Identity;
using NoCTF.Domain.LiveSolo;
using NoCTF.Domain.Teams;
using NoCTF.Infrastructure.Persistence;
using Testcontainers.PostgreSql;

namespace NoCTF.Tests.Integration.Persistence;

[Category("Integration")]
public sealed class LiveSoloFoundationPersistenceTests
{
    [Test, Timeout(300_000)]
    public async Task Generated_migration_preserves_old_modes_and_scoped_relations_enforce_order_and_active_team_uniqueness(CancellationToken ct)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var postgres = new PostgreSqlBuilder("postgres:17.10-alpine3.24@sha256:742f40ea20b9ff2ff31db5458d127452988a2164df9e17441e191f3b72252193").Build();
            await postgres.StartAsync(ct);
            var options = new DbContextOptionsBuilder<NoCtfDbContext>().UseNpgsql(postgres.GetConnectionString(),
                provider => provider.MigrationsAssembly("NoCTF.Persistence.PostgreSql")).UseSnakeCaseNamingConvention().Options;
            await using var db = new NoCtfDbContext(options);
            var migrations = db.Database.GetMigrations().ToArray();
            var previous = migrations[^2]; var newest = migrations[^1];
            await Assert.That(newest).Contains("LiveSoloFoundation");
            await db.GetService<IMigrator>().MigrateAsync(previous, ct);
            var now = DateTimeOffset.UtcNow;
            var user = new User { Id = Guid.NewGuid(), UserName = "livesolo-owner", Email = "livesolo@example.test", PasswordHash = "unused",
                Role = UserRole.Administrator, AccountStatus = UserAccountStatus.Active, CreatedAt = now };
            db.Users.Add(user);
            var old = new[] { GameMode.Ctf, GameMode.Awd, GameMode.Awdp, GameMode.Koh }.Select(mode => {
                var competition = CompetitionGeneratedCatalog.Create(mode); competition.Id = Guid.NewGuid(); competition.OwnerId = user.Id;
                competition.Title = mode.ToString(); competition.StartAt = now; competition.EndAt = now.AddHours(1);
                competition.CreatedAt = now; competition.UpdatedAt = now; competition.FlagDerivationSecret = new byte[32];
                competition.ModeConfiguration = CompetitionModeConfigurationDefaults.Create(mode, competition.Id); return competition;
            }).ToArray();
            db.Competitions.AddRange(old); await db.SaveChangesAsync(ct);
            db.ChangeTracker.Clear(); await db.Database.MigrateAsync(ct);
            await Assert.That(await db.Competitions.Select(x => x.Mode).ToArrayAsync(ct)).IsEquivalentTo(old.Select(x => x.Mode));
            var live = new LiveSoloCompetition { Id = Guid.NewGuid(), OwnerId = user.Id, Title = "LiveSolo", StartAt = now, EndAt = now.AddHours(1),
                CreatedAt = now, UpdatedAt = now, FlagDerivationSecret = new byte[32] };
            live.ModeConfiguration = CompetitionModeConfigurationDefaults.Create(GameMode.LiveSolo, live.Id);
            var template = new LiveSoloChallenge { Id = Guid.NewGuid(), OwnerId = user.Id, Title = "First Flag", CreatedAt = now, UpdatedAt = now };
            template.Definition = new LiveSoloChallengeDefinition { ChallengeId = template.Id };
            var challenge = new LiveSoloCompetitionChallenge { Id = Guid.NewGuid(), CompetitionId = live.Id, ChallengeId = template.Id,
                Rules = new LiveSoloCompetitionChallengeRules(), UpdatedAt = now };
            challenge.Rules.CompetitionChallengeId = challenge.Id;
            var team = new Team { Id = Guid.NewGuid(), CompetitionId = live.Id, Name = "Solo", CaptainId = user.Id, MemberIds = [user.Id],
                InvitationToken = new string('l', 32), RegistrationStatus = TeamRegistrationStatus.Approved, RegisteredAt = now };
            var group = new LiveSoloQuestionGroup { Id = Guid.NewGuid(), CompetitionId = live.Id, Name = "Round 1",
                Items = [new() { Position = 0, CompetitionChallengeId = challenge.Id }] };
            var match = new LiveSoloMatch { Id = Guid.NewGuid(), CompetitionId = live.Id, Stage = 1, CreatedAt = now,
                Slots = [new() { Side = LiveSoloSide.Left, Source = LiveSoloSlotSource.Seed, TeamId = team.Id, Resolved = true },
                    new() { Side = LiveSoloSide.Right, Source = LiveSoloSlotSource.Bye, Resolved = true }] };
            db.Competitions.Add(live); db.Challenges.Add(template); db.CompetitionChallenges.Add(challenge); db.Teams.Add(team);
            db.LiveSoloQuestionGroups.Add(group); db.LiveSoloMatches.Add(match); await db.SaveChangesAsync(ct);
            var round = new LiveSoloRound { Id = Guid.NewGuid(), MatchId = match.Id, Number = 1, QuestionGroupId = group.Id, CreatedAt = now,
                Questions = [new() { Id = Guid.NewGuid(), CompetitionChallengeId = challenge.Id }] };
            db.LiveSoloRounds.Add(round); db.LiveSoloActiveTeamSlots.Add(new() { CompetitionId = live.Id, TeamId = team.Id, MatchId = match.Id });
            await db.SaveChangesAsync(ct);
            match.CurrentRoundId = round.Id; await db.SaveChangesAsync(ct);
            var question = round.Questions.Single();
            var fact = new FlagAttemptGameplayFact { Id = Guid.NewGuid(), CompetitionId = live.Id, CompetitionChallengeId = challenge.Id,
                TeamId = team.Id, ActorUserId = user.Id, State = GameplayFactState.Queued, OccurredAt = now, UpdatedAt = now };
            db.GameplayFacts.Add(fact); db.LiveSoloSubmissions.Add(new() { GameplayFactId = fact.Id, RoundId = round.Id,
                RoundQuestionId = question.Id, AdmissionSequence = 1 }); await db.SaveChangesAsync(ct);
            await using (var duplicate = new NoCtfDbContext(options))
            {
                duplicate.LiveSoloActiveTeamSlots.Add(new() { CompetitionId = live.Id, TeamId = team.Id, MatchId = match.Id });
                await Assert.That(async () => await duplicate.SaveChangesAsync(ct)).Throws<DbUpdateException>();
            }
            await using (var duplicate = new NoCtfDbContext(options))
            {
                var otherFact = new FlagAttemptGameplayFact { Id = Guid.NewGuid(), CompetitionId = live.Id,
                    CompetitionChallengeId = challenge.Id, TeamId = team.Id, State = GameplayFactState.Queued, OccurredAt = now, UpdatedAt = now };
                duplicate.GameplayFacts.Add(otherFact); duplicate.LiveSoloSubmissions.Add(new() { GameplayFactId = otherFact.Id,
                    RoundId = round.Id, RoundQuestionId = question.Id, AdmissionSequence = 1 });
                await Assert.That(async () => await duplicate.SaveChangesAsync(ct)).Throws<DbUpdateException>();
                duplicate.ChangeTracker.Clear();
                await Assert.That(await duplicate.GameplayFacts.AnyAsync(x => x.Id == otherFact.Id, ct)).IsFalse();
            }
            db.ChangeTracker.Clear();
            var loaded = await db.LiveSoloMatches.Include(x => x.Slots).Include(x => x.Rounds).ThenInclude(x => x.Questions).SingleAsync(ct);
            await Assert.That(loaded.Slots.Count).IsEqualTo(2);
            await Assert.That(loaded.Rounds.Single().Questions.Single().Id).IsEqualTo(question.Id);
            await Assert.That(((LiveSoloCompetitionModeConfiguration)(await db.Competitions.SingleAsync(x => x.Id == live.Id, ct)).ModeConfiguration!).Enabled).IsFalse();
        });
    }
}
