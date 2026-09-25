using Microsoft.EntityFrameworkCore;
using NSubstitute;
using NoCTF.Application.Competitions.Events;
using NoCTF.Application.Competitions.Management;
using NoCTF.Application.Competitions.Tracks;
using NoCTF.Application.Messaging;
using NoCTF.Application.Teams.Registration;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Identity;
using NoCTF.Domain.Teams;
using NoCTF.GameModes.Registration;
using NoCTF.Infrastructure.Competitions.Events;
using NoCTF.Infrastructure.Competitions.Management;
using NoCTF.Infrastructure.Competitions.Tracks;
using NoCTF.Infrastructure.Persistence;
using NoCTF.Infrastructure.Teams.Registration;
using Testcontainers.PostgreSql;

namespace NoCTF.Tests.Integration.Persistence;

[Category("Integration")]
[Category("CompetitionTracks")]
public sealed class CompetitionTrackPersistenceTests
{
    [Test]
    [Timeout(300_000)]
    public async Task Track_configuration_assignment_reassignment_and_lifecycle_are_transactional(
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
            var participantId = Guid.CreateVersion7(now.AddTicks(1));
            var teamId = Guid.CreateVersion7(now);
            var competitionId = Guid.CreateVersion7(now);

            await using (var seed = new NoCtfDbContext(options))
            {
                await seed.Database.EnsureCreatedAsync(cancellationToken);
                seed.Users.Add(new User
                {
                    Id = ownerId,
                    UserName = "track-owner",
                    NormalizedUserName = "TRACK-OWNER",
                    Email = "track-owner@example.test",
                    PasswordHash = "test",
                    Role = UserRole.Organizer,
                    CreatedAt = now,
                    UpdatedAt = now
                });
                seed.Users.Add(new User
                {
                    Id = participantId,
                    UserName = "track-participant",
                    NormalizedUserName = "TRACK-PARTICIPANT",
                    Email = "track-participant@example.test",
                    PasswordHash = "test",
                    Role = UserRole.User,
                    CreatedAt = now,
                    UpdatedAt = now
                });
                seed.Competitions.Add(new CtfCompetition
                {
                    Id = competitionId,
                    Title = "Track persistence",
                    OwnerId = ownerId,
                    Status = CompetitionStatus.Published,
                    TracksEnabled = true,
                    Tracks = CompetitionTrackConfiguration.ToPersisted(
                        CompetitionTrackConfiguration.DefaultFor(GameMode.Ctf),
                        competitionId),
                    ModeConfiguration = TestConfigurations.Competition(GameMode.Ctf),
                    StartAt = now.AddHours(1),
                    EndAt = now.AddHours(2),
                    FlagDerivationSecret = Enumerable.Range(1, 32).Select(value => (byte)value).ToArray(),
                    CreatedAt = now,
                    UpdatedAt = now,
                });
                seed.Teams.Add(new Team
                {
                    Id = teamId,
                    CompetitionId = competitionId,
                    Name = "Internal operators",
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
                var defaultConfiguration = CompetitionTrackConfiguration.FromPersisted(
                    createdCompetition.Mode,
                    createdCompetition.Tracks);
                await Assert.That(defaultConfiguration.Tracks).HasSingleItem();
                await Assert.That(defaultConfiguration.DefaultTrack.Key)
                    .IsEqualTo(CompetitionTrackConfiguration.DefaultTrackKey);
                await Assert.That(defaultConfiguration.DefaultTrack.EarnsScore).IsTrue();
                await Assert.That(createdCompetition.TracksEnabled).IsFalse();
            }

            var tracks = new[]
            {
                Track("default", "Official", isDefault: true),
                Track("invite", "Invitation only"),
                Track("internal", "Internal", isInternal: true)
            };
            await using (var updateDb = new NoCtfDbContext(options))
            {
                var store = CreateStore(updateDb);
                var updated = await store.UpdateAsync(new(
                    competitionId,
                    true,
                    tracks,
                    [],
                    ownerId,
                    now,
                    [new("invite", "invite-only", ClearInvitationCode: false)]),
                    cancellationToken);
                await Assert.That(updated.Succeeded).IsTrue();

                var assigned = await store.AssignAsync(new(
                    competitionId,
                    teamId,
                    " INTERNAL ",
                    ownerId,
                    now.AddSeconds(1)), cancellationToken);
                await Assert.That(assigned.Succeeded).IsTrue();
            }

            await using (var readDb = new NoCtfDbContext(options))
            {
                var store = CreateStore(readDb);
                var anonymous = await store.GetAsync(
                    competitionId,
                    null,
                    includeInternal: false,
                    includeInvitationCodes: false,
                    cancellationToken);
                await Assert.That(anonymous!.Tracks.Select(track => track.Key))
                    .IsEquivalentTo(["default", "invite"]);
                await Assert.That(anonymous.Tracks.All(track => track.InvitationCode is null))
                    .IsTrue();

                var participant = await store.GetAsync(
                    competitionId,
                    ownerId,
                    includeInternal: false,
                    includeInvitationCodes: false,
                    cancellationToken);
                await Assert.That(participant!.ViewerTeamId).IsEqualTo(teamId);
                await Assert.That(participant.Tracks.Select(track => track.Key))
                    .IsEquivalentTo(["default", "invite"]);
                await Assert.That(participant.Tracks.Any(track => track.IsInternal)).IsFalse();
                await Assert.That(participant.Tracks.All(track => track.InvitationCode is null))
                    .IsTrue();

                var staff = await store.GetAsync(
                    competitionId,
                    ownerId,
                    includeInternal: true,
                    includeInvitationCodes: true,
                    cancellationToken);
                await Assert.That(staff!.Tracks.Select(track => track.Key))
                    .IsEquivalentTo(["default", "invite", "internal"]);
                await Assert.That(staff.Tracks.Single(track => track.Key == "invite").InvitationCode)
                    .IsEqualTo("invite-only");
            }

            await using (var conflictDb = new NoCtfDbContext(options))
            {
                var result = await CreateStore(conflictDb).UpdateAsync(new(
                    competitionId,
                    true,
                    [Track("default", "Official", isDefault: true)],
                    [],
                    ownerId,
                    now.AddSeconds(2)), cancellationToken);
                await Assert.That(result.FailureCode)
                    .IsEqualTo(CompetitionTrackFailureCode.TrackReassignmentRequired);
                await Assert.That(result.AffectedTeamCount).IsEqualTo(1);
            }

            await using (var migrateDb = new NoCtfDbContext(options))
            {
                var result = await CreateStore(migrateDb).UpdateAsync(new(
                    competitionId,
                    true,
                    [Track("default", "Official", isDefault: true), Track("invite", "Invitation only")],
                    [new("internal", "default")],
                    ownerId,
                    now.AddSeconds(2)), cancellationToken);
                await Assert.That(result.Succeeded).IsTrue();
            }

            await using (var migratedVerify = new NoCtfDbContext(options))
            {
                var migratedCompetition = await migratedVerify.Competitions.AsNoTracking()
                    .SingleAsync(item => item.Id == competitionId, cancellationToken);
                var migratedTeam = await migratedVerify.Teams.AsNoTracking()
                    .SingleAsync(item => item.Id == teamId, cancellationToken);
                var migratedConfiguration = CompetitionTrackConfiguration.FromPersisted(
                    migratedCompetition.Mode,
                    migratedCompetition.Tracks);
                await Assert.That(migratedConfiguration.Find("internal")).IsNull();
                await Assert.That(migratedTeam.TrackKey).IsEqualTo("default");
            }

            await using (var restoreAndDisableDb = new NoCtfDbContext(options))
            {
                var store = CreateStore(restoreAndDisableDb);
                await Assert.That((await store.UpdateAsync(new(
                    competitionId,
                    true,
                    tracks,
                    [],
                    ownerId,
                    now.AddSeconds(3)), cancellationToken)).Succeeded).IsTrue();
                await Assert.That((await store.AssignAsync(new(
                    competitionId,
                    teamId,
                    "internal",
                    ownerId,
                    now.AddSeconds(4)), cancellationToken)).Succeeded).IsTrue();

                var disabled = await store.UpdateAsync(new(
                    competitionId,
                    false,
                    tracks,
                    [],
                    ownerId,
                    now.AddSeconds(5)), cancellationToken);
                await Assert.That(disabled.Succeeded).IsTrue();
                await Assert.That(disabled.Value!.Enabled).IsFalse();

                var disabledAssignment = await store.AssignAsync(new(
                    competitionId,
                    teamId,
                    "internal",
                    ownerId,
                    now.AddSeconds(6)), cancellationToken);
                await Assert.That(disabledAssignment.FailureCode)
                    .IsEqualTo(CompetitionTrackFailureCode.TracksDisabled);

                var registration = await new CreateTeam(new TeamRegistrationStore(
                        restoreAndDisableDb,
                        Substitute.For<IPostCommitMessagePublisher>()))
                    .ExecuteAsync(new(
                        competitionId,
                        participantId,
                        "No track selection",
                        now.AddSeconds(6),
                        TrackKey: null,
                        TrackInvitationCode: "ignored"), cancellationToken);
                await Assert.That(registration.Succeeded).IsTrue();
                await Assert.That(registration.Value!.TrackKey).IsEqualTo("default");
            }

            await using (var disabledVerify = new NoCtfDbContext(options))
            {
                await Assert.That(await disabledVerify.Teams.AsNoTracking()
                    .Where(item => item.Id == teamId)
                    .Select(item => item.TrackKey)
                    .SingleAsync(cancellationToken)).IsEqualTo("default");
            }

            await using (var reenableDb = new NoCtfDbContext(options))
            {
                var store = CreateStore(reenableDb);
                await Assert.That((await store.UpdateAsync(new(
                    competitionId,
                    true,
                    tracks,
                    [],
                    ownerId,
                    now.AddSeconds(7)), cancellationToken)).Succeeded).IsTrue();
                await Assert.That((await store.UpdateAsync(new(
                    competitionId,
                    true,
                    tracks,
                    [],
                    ownerId,
                    now.AddSeconds(8),
                    [new("invite", "a-new-secret", ClearInvitationCode: false)]),
                    cancellationToken)).Succeeded).IsTrue();
                await Assert.That((await store.AssignAsync(new(
                    competitionId,
                    teamId,
                    "internal",
                    ownerId,
                    now.AddSeconds(9)), cancellationToken)).Succeeded).IsTrue();
            }

            await using (var raceSetupDb = new NoCtfDbContext(options))
            {
                await Assert.That((await CreateStore(raceSetupDb).AssignAsync(new(
                    competitionId,
                    teamId,
                    "default",
                    ownerId,
                    now.AddSeconds(10)), cancellationToken)).Succeeded).IsTrue();
            }

            await using (var assignmentRaceDb = new NoCtfDbContext(options))
            await using (var deletionRaceDb = new NoCtfDbContext(options))
            {
                var assignmentTask = CreateStore(assignmentRaceDb).AssignAsync(new(
                    competitionId,
                    teamId,
                    "internal",
                    ownerId,
                    now.AddSeconds(11)), cancellationToken);
                var deletionTask = CreateStore(deletionRaceDb).UpdateAsync(new(
                    competitionId,
                    true,
                    [Track("default", "Official", isDefault: true), Track("invite", "Invitation only")],
                    [],
                    ownerId,
                    now.AddSeconds(11)), cancellationToken);
                await Task.WhenAll(assignmentTask, deletionTask);
            }

            await using (var raceVerifyDb = new NoCtfDbContext(options))
            {
                var competition = await raceVerifyDb.Competitions.AsNoTracking()
                    .SingleAsync(item => item.Id == competitionId, cancellationToken);
                var configuredKeys = CompetitionTrackConfiguration.FromPersisted(
                        competition.Mode,
                        competition.Tracks)
                    .Tracks.Select(track => track.Key)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);
                var assignedKeys = await raceVerifyDb.Teams.AsNoTracking()
                    .Where(item => item.CompetitionId == competitionId && item.DeletedAt == null)
                    .Select(item => item.TrackKey)
                    .ToArrayAsync(cancellationToken);
                await Assert.That(assignedKeys.All(configuredKeys.Contains)).IsTrue();
            }

            await using (var raceRestoreDb = new NoCtfDbContext(options))
            {
                var store = CreateStore(raceRestoreDb);
                await Assert.That((await store.UpdateAsync(new(
                    competitionId,
                    true,
                    tracks,
                    [],
                    ownerId,
                    now.AddSeconds(12)), cancellationToken)).Succeeded).IsTrue();
                await Assert.That((await store.AssignAsync(new(
                    competitionId,
                    teamId,
                    "internal",
                    ownerId,
                    now.AddSeconds(13)), cancellationToken)).Succeeded).IsTrue();
            }

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
                        ownerId,
                        now.AddSeconds(3)), cancellationToken);
                var canUpdate = frozenStatus != CompetitionStatus.Finished;
                await Assert.That(assignment.Succeeded).IsEqualTo(canUpdate);
                if (canUpdate)
                    await Assert.That(assignment.Value!.TrackKey).IsEqualTo("default");
                else
                    await Assert.That(assignment.FailureCode)
                        .IsEqualTo(CompetitionTrackFailureCode.CompetitionFinished);
                var update = await store.UpdateAsync(new(
                        competitionId,
                        true,
                        tracks,
                        [],
                        ownerId,
                        now.AddSeconds(4)), cancellationToken);
                await Assert.That(update.Succeeded).IsEqualTo(canUpdate);
                if (!canUpdate)
                {
                    await Assert.That(update.FailureCode)
                        .IsEqualTo(CompetitionTrackFailureCode.CompetitionFinished);
                    continue;
                }

                var addedTrackKey = $"live-{frozenStatus.ToString().ToLowerInvariant()}";
                var expandedTracks = tracks.Append(Track(
                    addedTrackKey,
                    $"Added while {frozenStatus}"))
                    .ToArray();
                var addWhileActive = await store.UpdateAsync(new(
                    competitionId,
                    true,
                    expandedTracks,
                    [],
                    ownerId,
                    now.AddSeconds(5)), cancellationToken);
                await Assert.That(addWhileActive.Succeeded).IsTrue();
                await Assert.That(addWhileActive.Value!.Tracks.Any(track =>
                    track.Key == addedTrackKey)).IsTrue();

                var deleteWhileActive = await store.UpdateAsync(new(
                    competitionId,
                    true,
                    tracks,
                    [new(addedTrackKey, "default")],
                    ownerId,
                    now.AddSeconds(6)), cancellationToken);
                await Assert.That(deleteWhileActive.Succeeded).IsTrue();
                await Assert.That(deleteWhileActive.Value!.Tracks.Any(track =>
                    track.Key == addedTrackKey)).IsFalse();

                await using var restoreDb = new NoCtfDbContext(options);
                var restored = await CreateStore(restoreDb).AssignAsync(new(
                    competitionId,
                    teamId,
                    "internal",
                    ownerId,
                    now.AddSeconds(7)), cancellationToken);
                await Assert.That(restored.Succeeded).IsTrue();
            }

            await using var verify = new NoCtfDbContext(options);
            var team = await verify.Teams.AsNoTracking()
                .SingleAsync(item => item.Id == teamId, cancellationToken);
            var eventKinds = await verify.CompetitionEvents.AsNoTracking()
                .Where(item => item.CompetitionId == competitionId)
                .OrderBy(item => item.OccurredAt)
                .Select(item => item.Kind)
                .ToArrayAsync(cancellationToken);
            await Assert.That(team.TrackKey).IsEqualTo("internal");
            await Assert.That(eventKinds.Count(kind =>
                    kind == NoCTF.Domain.Competitions.Events.CompetitionEventKind.TrackConfigurationUpdated))
                .IsGreaterThanOrEqualTo(5);
            await Assert.That(eventKinds.Count(kind =>
                    kind == NoCTF.Domain.Competitions.Events.CompetitionEventKind.TrackRegistrationPolicyUpdated))
                .IsEqualTo(1);
            await Assert.That(eventKinds.Count(kind =>
                    kind == NoCTF.Domain.Competitions.Events.CompetitionEventKind.TeamTrackChanged))
                .IsGreaterThanOrEqualTo(9);
            var payloads = await verify.CompetitionEvents.AsNoTracking()
                .SelectMany(item => item.TrackKeys.Select(key => key.Value))
                .ToArrayAsync(cancellationToken);
            await Assert.That(payloads.Any(payload => payload.Contains(
                "a-new-secret",
                StringComparison.Ordinal))).IsFalse();
        });
    }

    private static CompetitionTrackStore CreateStore(NoCtfDbContext db)
    {
        var outbox = Substitute.For<IPostCommitMessagePublisher>();
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
