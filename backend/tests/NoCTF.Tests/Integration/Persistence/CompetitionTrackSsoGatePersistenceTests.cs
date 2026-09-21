using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Competitions.Tracks;
using NoCTF.Application.Teams.Membership;
using NoCTF.Application.Teams.Registration;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Identity;
using NoCTF.Domain.Platform;
using NoCTF.Domain.Teams;
using NoCTF.Infrastructure.Competitions.Events;
using NoCTF.Infrastructure.Competitions.Tracks;
using NoCTF.Infrastructure.Messaging;
using NoCTF.Infrastructure.Persistence;
using NoCTF.Infrastructure.Teams.Membership;
using NoCTF.Infrastructure.Teams.Registration;
using Testcontainers.PostgreSql;

namespace NoCTF.Tests.Integration.Persistence;

[Category("Integration")]
public sealed class CompetitionTrackSsoGatePersistenceTests
{
    [Test]
    [Timeout(300_000)]
    public async Task Gate_checks_team_entry_but_does_not_retroactively_reject_existing_teams(
        CancellationToken cancellationToken)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var postgres = new PostgreSqlBuilder(
                    "postgres:17.10-alpine3.24@sha256:742f40ea20b9ff2ff31db5458d127452988a2164df9e17441e191f3b72252193")
                .WithDatabase("noctf_track_sso_gate")
                .WithUsername("postgres")
                .WithPassword("postgres")
                .Build();
            await postgres.StartAsync(cancellationToken);
            var options = new DbContextOptionsBuilder<NoCtfDbContext>()
                .UseNpgsql(postgres.GetConnectionString())
                .UseSnakeCaseNamingConvention()
                .Options;
            await using var db = new NoCtfDbContext(options);
            await db.Database.EnsureCreatedAsync(cancellationToken);

            var now = DateTimeOffset.UtcNow;
            var competitionId = Guid.CreateVersion7(now);
            var providerId = Guid.CreateVersion7(now.AddMilliseconds(1));
            var eligibleCaptainId = Guid.CreateVersion7(now.AddMilliseconds(2));
            var eligibleMemberId = Guid.CreateVersion7(now.AddMilliseconds(3));
            var unboundUserId = Guid.CreateVersion7(now.AddMilliseconds(4));
            var secondUnboundUserId = Guid.CreateVersion7(now.AddMilliseconds(5));
            var settings = await db.PlatformSettings.SingleAsync(cancellationToken);
            settings.SsoConfiguration.Providers.Add(new SsoProviderConfiguration
            {
                Id = providerId,
                Name = "School identity",
                Protocol = SsoProtocol.Cas,
                Enabled = true,
                AllowBinding = true,
                AllowedHosts = ["cas.example.test"],
                Cas = new CasSsoProviderConfiguration
                {
                    IdentityNamespace = "school",
                    LoginUrl = "https://cas.example.test/login",
                    ServiceValidateUrl = "https://cas.example.test/serviceValidate"
                }
            });
            db.Users.AddRange(
                BoundUser(eligibleCaptainId, "eligible-captain", providerId, now),
                BoundUser(eligibleMemberId, "eligible-member", providerId, now),
                User(unboundUserId, "unbound", now),
                User(secondUnboundUserId, "second-unbound", now));
            var gatedTrack = Track("school", providerId);
            var defaultTrack = Track("default", null, isDefault: true);
            db.Competitions.Add(new Competition
            {
                Id = competitionId,
                OwnerId = eligibleCaptainId,
                Title = "SSO gate",
                Mode = GameMode.Ctf,
                Status = CompetitionStatus.Published,
                StartAt = now.AddHours(1),
                EndAt = now.AddHours(2),
                MaxTeamMembers = 5,
                ConfigurationJson = "{}",
                FlagDerivationSecret = new byte[32],
                TracksEnabled = true,
                TrackConfigurationJson = CompetitionTrackConfiguration.Serialize(new(
                    1,
                    [defaultTrack, gatedTrack])),
                CreatedAt = now,
                UpdatedAt = now
            });
            await db.SaveChangesAsync(cancellationToken);

            var outbox = new NoOpTransactionalMessageOutbox();
            var registrations = new TeamRegistrationStore(db, outbox);
            var eligibleTeam = await registrations.TryCreateAsync(new(
                competitionId,
                eligibleCaptainId,
                "Eligible team",
                now,
                gatedTrack.Key), TeamRegistrationStatus.Approved, cancellationToken);
            var blockedCreate = await registrations.TryCreateAsync(new(
                competitionId,
                unboundUserId,
                "Blocked team",
                now,
                gatedTrack.Key), TeamRegistrationStatus.Approved, cancellationToken);

            var invitation = await db.Teams.AsNoTracking()
                .Where(team => team.Id == eligibleTeam.Team!.Id)
                .Select(team => team.InvitationToken)
                .SingleAsync(cancellationToken);
            var memberships = new TeamMembershipStore(db, outbox);
            var blockedJoin = await memberships.JoinByInvitationAsync(
                competitionId,
                invitation,
                unboundUserId,
                now,
                cancellationToken);
            var allowedJoin = await memberships.JoinByInvitationAsync(
                competitionId,
                invitation,
                eligibleMemberId,
                now,
                cancellationToken);

            var defaultTeam = await registrations.TryCreateAsync(new(
                competitionId,
                unboundUserId,
                "Default team",
                now,
                defaultTrack.Key), TeamRegistrationStatus.Approved, cancellationToken);
            var tracks = new CompetitionTrackStore(
                db,
                outbox,
                new CompetitionEventStore(db, outbox));
            var unboundView = await tracks.GetAsync(
                competitionId,
                unboundUserId,
                includeInternal: false,
                includeInvitationCodes: false,
                cancellationToken);
            var blockedAssignment = await tracks.AssignAsync(new(
                competitionId,
                defaultTeam.Team!.Id,
                gatedTrack.Key,
                eligibleCaptainId,
                now), cancellationToken);
            var missingProviderUpdate = await tracks.UpdateAsync(new(
                competitionId,
                Enabled: true,
                Tracks:
                [
                    defaultTrack,
                    gatedTrack with { RequiredSsoProviderId = Guid.NewGuid() }
                ],
                RemovedTrackReassignments: [],
                ActorUserId: eligibleCaptainId,
                UpdatedAt: now.AddSeconds(30)), cancellationToken);
            var grandfatheredUpdate = await tracks.UpdateAsync(new(
                competitionId,
                Enabled: true,
                Tracks:
                [
                    defaultTrack with { RequiredSsoProviderId = providerId },
                    gatedTrack
                ],
                RemovedTrackReassignments: [],
                ActorUserId: eligibleCaptainId,
                UpdatedAt: now.AddMinutes(1)), cancellationToken);
            await db.Competitions.Where(competition => competition.Id == competitionId)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(competition => competition.TracksEnabled, false),
                    cancellationToken);
            db.ChangeTracker.Clear();
            var disabledGateCreate = await registrations.TryCreateAsync(new(
                competitionId,
                secondUnboundUserId,
                "Disabled gate team",
                now.AddMinutes(2),
                TrackKey: null), TeamRegistrationStatus.Approved, cancellationToken);

            await Assert.That(eligibleTeam.Team).IsNotNull();
            await Assert.That(blockedCreate.Failure)
                .IsEqualTo(TeamRegistrationFailure.TrackSsoIdentityRequired);
            await Assert.That(blockedJoin)
                .IsEqualTo(TeamMembershipFailure.TrackSsoIdentityRequired);
            await Assert.That(allowedJoin).IsNull();
            await Assert.That(unboundView!.Tracks.Single(track => track.Key == gatedTrack.Key)
                .MeetsSsoRequirement).IsFalse();
            await Assert.That(unboundView.Tracks.Single(track => track.Key == gatedTrack.Key)
                .RequiredSsoProviderName).IsEqualTo("School identity");
            await Assert.That(blockedAssignment.FailureCode)
                .IsEqualTo(CompetitionTrackFailureCode.TrackSsoIdentityRequired);
            await Assert.That(missingProviderUpdate.FailureCode)
                .IsEqualTo(CompetitionTrackFailureCode.SsoProviderNotFound);
            await Assert.That(grandfatheredUpdate.Succeeded).IsTrue();
            await Assert.That(disabledGateCreate.Team).IsNotNull();
            await Assert.That((await db.Teams.AsNoTracking().SingleAsync(
                team => team.Id == defaultTeam.Team.Id,
                cancellationToken)).TrackKey).IsEqualTo(defaultTrack.Key);
        });
    }

    private static CompetitionTrackDefinition Track(
        string key,
        Guid? providerId,
        bool isDefault = false) => new(
        key,
        key,
        isDefault,
        IsPublicSelectable: true,
        IsInternal: false,
        EarnsScore: true,
        EarnsBlood: true,
        AffectsDynamicChallengeScore: true,
        VisibleOnLeaderboard: true,
        AffectsCompetitiveResults: true,
        RequiredSsoProviderId: providerId);

    private static User BoundUser(
        Guid id,
        string userName,
        Guid providerId,
        DateTimeOffset now)
    {
        var user = User(id, userName, now);
        user.ExternalIdentityProviderId = providerId;
        user.ExternalIdentityProtocol = SsoProtocol.Cas;
        user.ExternalIdentityNamespace = "school";
        user.ExternalIdentitySubject = userName;
        user.ExternalIdentityBoundAt = now;
        return user;
    }

    private static User User(Guid id, string userName, DateTimeOffset now) => new()
    {
        Id = id,
        UserName = userName,
        NormalizedUserName = userName.ToUpperInvariant(),
        Email = $"{userName}@example.test",
        PasswordHash = "unused",
        Kind = UserKind.Human,
        Role = UserRole.User,
        AccountStatus = UserAccountStatus.Active,
        CreatedAt = now,
        UpdatedAt = now
    };
}
