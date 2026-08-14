using Microsoft.EntityFrameworkCore;
using NSubstitute;
using NoCTF.Application.Competitions.Events;
using NoCTF.Application.Competitions.Management;
using NoCTF.Application.Competitions.Tracks;
using NoCTF.Application.Messaging;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Identity;
using NoCTF.Domain.Teams;
using NoCTF.GameModes.Registration;
using NoCTF.Infrastructure.Competitions.Events;
using NoCTF.Infrastructure.Competitions.Management;
using NoCTF.Infrastructure.Competitions.Tracks;
using NoCTF.Infrastructure.Persistence;
using Testcontainers.PostgreSql;

namespace NoCTF.Tests.Integration.Persistence;

[Category("Integration")]
[Category("CompetitionTracks")]
public sealed class CompetitionTrackPersistenceTests
{
    [Test]
    [Timeout(300_000)]
    public async Task Track_configuration_assignment_visibility_and_freeze_are_transactional(
        CancellationToken cancellationToken)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var postgres = new PostgreSqlBuilder("postgres:17.10-alpine3.24@sha256:742f40ea20b9ff2ff31db5458d127452988a2164df9e17441e191f3b72252193")
                .WithDatabase("noctf_competition_tracks")
                .WithUsername("postgres")
                .WithPassword("postgres")
                .Build();
            await postgres.StartAsync(cancellationToken);
            var options = new DbContextOptionsBuilder<NoCtfDbContext>()
                .UseNpgsql(postgres.GetConnectionString())
                .UseSnakeCaseNamingConvention()
                .Options;
            var now = DateTimeOffset.UtcNow;
            var ownerId = Guid.CreateVersion7(now);
            var teamId = Guid.CreateVersion7(now);
            var competitionId = Guid.CreateVersion7(now);

            await using (var seed = new NoCtfDbContext(options))
            {
                await seed.Database.MigrateAsync(cancellationToken);
                seed.Users.Add(new User
                {
                    Id = ownerId,
                    UserName = "track-owner",
                    NormalizedUserName = "TRACK-OWNER",
                    Email = "track-owner@example.test",
                    NormalizedEmail = "TRACK-OWNER@EXAMPLE.TEST",
                    PasswordHash = "test",
                    Role = UserRole.Organizer,
                    CreatedAt = now,
                    UpdatedAt = now
                });
                seed.Competitions.Add(new Competition
                {
                    Id = competitionId,
                    Title = "Track persistence",
                    OwnerId = ownerId,
                    Mode = GameMode.Ctf,
                    Status = CompetitionStatus.Published,
                    ConfigurationJson = GameModeDefaultConfiguration.GetCompetitionJson(GameMode.Ctf),
                    StartAt = now.AddHours(1),
                    EndAt = now.AddHours(2),
                    FlagDerivationSecret = Enumerable.Range(1, 32).Select(value => (byte)value).ToArray(),
                    CreatedAt = now,
                    UpdatedAt = now,
                    ConfigurationUpdatedAt = now
                });
                seed.Teams.Add(new Team
                {
                    Id = teamId,
                    CompetitionId = competitionId,
                    Name = "Internal operators",
                    NormalizedName = "INTERNAL OPERATORS",
                    CaptainId = ownerId,
                    MemberIds = [ownerId],
                    InvitationToken = new string('a', 32),
                    RegistrationStatus = TeamRegistrationStatus.Approved,
                    RegisteredAt = now
                });
                await seed.SaveChangesAsync(cancellationToken);
            }

            await using (var createDb = new NoCtfDbContext(options))
            {
                var created = await new CompetitionManagementStore(createDb).CreateAsync(new(
                    "Default track creation",
                    null,
                    GameMode.Ctf,
                    now.AddHours(3),
                    now.AddHours(4),
                    TeamRegistrationAutoApprove: true,
                    MaxTeamMembers: 5,
                    MaxConcurrentRuntimeInstancesPerTeam: 1,
                    ownerId,
                    now), cancellationToken);
                await Assert.That(created.Succeeded).IsTrue();

                var createdCompetition = await createDb.Competitions.AsNoTracking()
                    .SingleAsync(item => item.Id == created.Competition!.Id, cancellationToken);
                var defaultConfiguration = CompetitionTrackConfiguration.ParseOrDefault(
                    createdCompetition.Mode,
                    createdCompetition.TrackConfigurationJson);
                await Assert.That(defaultConfiguration.Tracks).HasSingleItem();
                await Assert.That(defaultConfiguration.DefaultTrack.Key)
                    .IsEqualTo(CompetitionTrackConfiguration.DefaultTrackKey);
                await Assert.That(defaultConfiguration.DefaultTrack.EarnsScore).IsTrue();
            }

            var tracks = new[]
            {
                Track("default", "Official", isDefault: true),
                Track("internal", "Internal", isInternal: true)
            };
            await using (var updateDb = new NoCtfDbContext(options))
            {
                var store = CreateStore(updateDb);
                var updated = await store.UpdateAsync(new(
                    competitionId,
                    ExpectedRevision: 0,
                    tracks,
                    ownerId,
                    now), cancellationToken);
                await Assert.That(updated.Succeeded).IsTrue();

                var assigned = await store.AssignAsync(new(
                    competitionId,
                    teamId,
                    " INTERNAL ",
                    ExpectedTeamVersion: 0,
                    ownerId,
                    now.AddSeconds(1)), cancellationToken);
                await Assert.That(assigned.Succeeded).IsTrue();
            }

            await using (var readDb = new NoCtfDbContext(options))
            {
                var store = CreateStore(readDb);
                var anonymous = await store.GetAsync(
                    competitionId, null, includeInternal: false, cancellationToken);
                await Assert.That(anonymous!.Tracks.Select(track => track.Key))
                    .IsEquivalentTo(["default"]);

                var participant = await store.GetAsync(
                    competitionId, ownerId, includeInternal: false, cancellationToken);
                await Assert.That(participant!.ViewerTeamId).IsEqualTo(teamId);
                await Assert.That(participant.Tracks.Single(track => track.Key == "internal").IsViewerTrack)
                    .IsTrue();

                var staff = await store.GetAsync(
                    competitionId, ownerId, includeInternal: true, cancellationToken);
                await Assert.That(staff!.Tracks.Select(track => track.Key))
                    .IsEquivalentTo(["default", "internal"]);
            }

            await using (var conflictDb = new NoCtfDbContext(options))
            {
                var result = await CreateStore(conflictDb).UpdateAsync(new(
                    competitionId,
                    ExpectedRevision: 1,
                    [Track("default", "Official", isDefault: true)],
                    ownerId,
                    now.AddSeconds(2)), cancellationToken);
                await Assert.That(result.FailureCode).IsEqualTo(CompetitionTrackFailureCode.TrackInUse);
            }

            long expectedTeamVersion = 1;
            foreach (var frozenStatus in new[]
                     {
                         CompetitionStatus.Running,
                         CompetitionStatus.Paused,
                         CompetitionStatus.Finished
                     })
            {
                await using (var statusDb = new NoCtfDbContext(options))
                {
                    await statusDb.Competitions
                        .Where(item => item.Id == competitionId)
                        .ExecuteUpdateAsync(setters => setters
                            .SetProperty(item => item.Status, frozenStatus), cancellationToken);
                }

                await using var freezeDb = new NoCtfDbContext(options);
                var store = CreateStore(freezeDb);
                var assignment = await store.AssignAsync(new(
                        competitionId,
                        teamId,
                        "default",
                        ExpectedTeamVersion: expectedTeamVersion,
                        ownerId,
                        now.AddSeconds(3)), cancellationToken);
                await Assert.That(assignment.Succeeded).IsTrue();
                await Assert.That(assignment.Value!.TrackKey).IsEqualTo("default");
                expectedTeamVersion = assignment.Value.TeamVersion;
                var update = await store.UpdateAsync(new(
                        competitionId,
                        ExpectedRevision: 1,
                        tracks,
                        ownerId,
                        now.AddSeconds(4)), cancellationToken);
                await Assert.That(update.FailureCode)
                    .IsEqualTo(CompetitionTrackFailureCode.ConfigurationLocked);

                await using var restoreDb = new NoCtfDbContext(options);
                var restored = await CreateStore(restoreDb).AssignAsync(new(
                    competitionId,
                    teamId,
                    "internal",
                    ExpectedTeamVersion: assignment.Value.TeamVersion,
                    ownerId,
                    now.AddSeconds(5)), cancellationToken);
                await Assert.That(restored.Succeeded).IsTrue();
                expectedTeamVersion = restored.Value!.TeamVersion;
            }

            await using var verify = new NoCtfDbContext(options);
            var team = await verify.Teams.AsNoTracking()
                .SingleAsync(item => item.Id == teamId, cancellationToken);
            var competition = await verify.Competitions.AsNoTracking()
                .SingleAsync(item => item.Id == competitionId, cancellationToken);
            var eventKinds = await verify.CompetitionEvents.AsNoTracking()
                .Where(item => item.CompetitionId == competitionId)
                .OrderBy(item => item.OccurredAt)
                .Select(item => item.Kind)
                .ToArrayAsync(cancellationToken);
            await Assert.That(team.TrackKey).IsEqualTo("internal");
            await Assert.That(competition.TrackConfigurationRevision).IsEqualTo(1);
            await Assert.That(competition.LeaderboardDirty).IsTrue();
            await Assert.That(eventKinds.Count(kind =>
                    kind == NoCTF.Domain.Competitions.Events.CompetitionEventKind.TrackConfigurationUpdated))
                .IsEqualTo(1);
            await Assert.That(eventKinds.Count(kind =>
                    kind == NoCTF.Domain.Competitions.Events.CompetitionEventKind.TeamTrackChanged))
                .IsEqualTo(7);
        });
    }

    private static CompetitionTrackStore CreateStore(NoCtfDbContext db)
    {
        var outbox = Substitute.For<ITransactionalMessageOutbox>();
        ICompetitionEventRecorder events = new CompetitionEventStore(db, outbox);
        return new CompetitionTrackStore(db, outbox, events);
    }

    private static CompetitionTrackDefinition Track(
        string key,
        string name,
        bool isDefault = false,
        bool isInternal = false) => new(
        key,
        name,
        isDefault,
        IsPublicSelectable: !isInternal,
        IsInternal: isInternal,
        EarnsScore: !isInternal,
        EarnsBlood: !isInternal,
        AffectsDynamicChallengeScore: !isInternal,
        VisibleOnLeaderboard: !isInternal,
        AffectsCompetitiveResults: !isInternal);
}
