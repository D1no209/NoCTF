using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using NoCTF.Application.Authentication.Account;
using NoCTF.Application.Authentication.RefreshJwt;
using NoCTF.Application.Messaging;
using NoCTF.Application.Runtime.Instances;
using NoCTF.Domain.Challenges;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Gameplay;
using NoCTF.Domain.Identity;
using NoCTF.Domain.Storage;
using NoCTF.Domain.Teams;
using NoCTF.Infrastructure.Authentication;
using NoCTF.Infrastructure.Persistence;
using Testcontainers.PostgreSql;

namespace NoCTF.Tests.Integration.Authentication;

[Category("Integration")]
public sealed class UserProfilePersistenceTests
{
    [Test]
    [Timeout(300_000)]
    public async Task Public_profile_aggregates_only_public_approved_competition_activity(
        CancellationToken cancellationToken)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var postgres = new PostgreSqlBuilder("postgres:17.10-alpine3.24@sha256:742f40ea20b9ff2ff31db5458d127452988a2164df9e17441e191f3b72252193")
                .WithDatabase("noctf_public_user_profile")
                .WithUsername("postgres")
                .WithPassword("postgres")
                .Build();
            await postgres.StartAsync(cancellationToken);
            var options = new DbContextOptionsBuilder<NoCtfDbContext>()
                .UseNpgsql(postgres.GetConnectionString())
                .UseSnakeCaseNamingConvention()
                .Options;
            var hasher = new PasswordHasher<User>(Options.Create(new PasswordHasherOptions
            {
                IterationCount = 10_000
            }));
            var now = DateTimeOffset.UtcNow;
            var userId = Guid.CreateVersion7(now);
            var publicCompetitionId = Guid.CreateVersion7(now.AddSeconds(1));
            var privateCompetitionId = Guid.CreateVersion7(now.AddSeconds(2));
            var otherPublicCompetitionId = Guid.CreateVersion7(now.AddSeconds(9));
            var futureCompetitionId = Guid.CreateVersion7(now.AddSeconds(12));
            var publicTeamId = Guid.CreateVersion7(now.AddSeconds(3));
            var privateTeamId = Guid.CreateVersion7(now.AddSeconds(4));
            var publicChallengeId = Guid.CreateVersion7(now.AddSeconds(5));
            var privateChallengeId = Guid.CreateVersion7(now.AddSeconds(6));
            var unusedPublicChallengeId = Guid.CreateVersion7(now.AddSeconds(10));
            var futureChallengeId = Guid.CreateVersion7(now.AddSeconds(13));
            var publicCompetitionChallengeId = Guid.CreateVersion7(now.AddSeconds(7));
            var privateCompetitionChallengeId = Guid.CreateVersion7(now.AddSeconds(8));
            var unusedPublicCompetitionChallengeId = Guid.CreateVersion7(now.AddSeconds(11));
            var futureCompetitionChallengeId = Guid.CreateVersion7(now.AddSeconds(14));

            await using var db = new NoCtfDbContext(options);
            await db.Database.EnsureCreatedAsync(cancellationToken);
            var users = new AuthenticationStore(db, hasher);
            await users.CreateAsync(
                userId,
                "PublicPlayer",
                "public-player@example.test",
                "eight888",
                true,
                now,
                cancellationToken);

            db.Competitions.AddRange(
                Competition(publicCompetitionId, userId, CompetitionAccessMode.Public, "Public final", now),
                Competition(privateCompetitionId, userId, CompetitionAccessMode.StaffOnly, "Private final", now),
                Competition(otherPublicCompetitionId, userId, CompetitionAccessMode.Public, "Other public final", now),
                Competition(futureCompetitionId, userId, CompetitionAccessMode.Public, "Future public", now,
                    CompetitionStatus.Visible));
            db.Teams.AddRange(
                Team(publicTeamId, publicCompetitionId, userId, "Public team", now),
                Team(privateTeamId, privateCompetitionId, userId, "Private team", now));
            db.Challenges.AddRange(
                Challenge(publicChallengeId, userId, "Web", now),
                Challenge(privateChallengeId, userId, "Crypto", now),
                Challenge(unusedPublicChallengeId, userId, "Pwn", now),
                Challenge(futureChallengeId, userId, "Misc", now));
            db.CompetitionChallenges.AddRange(
                CompetitionChallenge(publicCompetitionChallengeId, publicCompetitionId, publicChallengeId, now),
                CompetitionChallenge(privateCompetitionChallengeId, privateCompetitionId, privateChallengeId, now),
                CompetitionChallenge(unusedPublicCompetitionChallengeId, otherPublicCompetitionId, unusedPublicChallengeId, now),
                CompetitionChallenge(futureCompetitionChallengeId, futureCompetitionId, futureChallengeId, now));
            db.GameplayFacts.AddRange(
                SuccessfulFact(publicCompetitionId, publicCompetitionChallengeId, publicTeamId, userId, now.AddMinutes(1)),
                SuccessfulFact(publicCompetitionId, publicCompetitionChallengeId, publicTeamId, userId, now.AddMinutes(2)),
                SuccessfulFact(privateCompetitionId, privateCompetitionChallengeId, privateTeamId, userId, now.AddMinutes(3)));
            await db.SaveChangesAsync(cancellationToken);

            var profile = await users.GetPublicProfileAsync(userId, cancellationToken);

            await Assert.That(profile).IsNotNull();
            await Assert.That(profile!.CompetitionCount).IsEqualTo(1);
            await Assert.That(profile.FinishedCompetitionCount).IsEqualTo(1);
            await Assert.That(profile.SuccessfulChallengeCount).IsEqualTo(1);
            await Assert.That(profile.Modes!).HasSingleItem();
            await Assert.That(profile.Modes![0]).IsEqualTo(new PublicUserModeSummary(GameMode.Ctf, 1));
            await Assert.That(profile.Directions!).Count().IsEqualTo(2);
            await Assert.That(profile.Directions![0]).IsEqualTo(new PublicUserDirectionSummary("Web", 1));
            await Assert.That(profile.Directions![1]).IsEqualTo(new PublicUserDirectionSummary("Pwn", 0));
            await Assert.That(profile.RecentCompetitions!).HasSingleItem();
            await Assert.That(profile.RecentCompetitions![0].CompetitionId).IsEqualTo(publicCompetitionId);
        });
    }

    [Test]
    [Timeout(300_000)]
    public async Task Description_avatar_profile_cover_and_wallpaper_preferences_survive_a_new_database_context(
        CancellationToken cancellationToken)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var postgres = new PostgreSqlBuilder("postgres:17.10-alpine3.24@sha256:742f40ea20b9ff2ff31db5458d127452988a2164df9e17441e191f3b72252193")
                .WithDatabase("noctf_user_profile")
                .WithUsername("postgres")
                .WithPassword("postgres")
                .Build();
            await postgres.StartAsync(cancellationToken);
            var options = new DbContextOptionsBuilder<NoCtfDbContext>()
                .UseNpgsql(postgres.GetConnectionString())
                .UseSnakeCaseNamingConvention()
                .Options;
            var hasher = new PasswordHasher<User>(Options.Create(new PasswordHasherOptions
            {
                IterationCount = 10_000
            }));
            var now = DateTimeOffset.UtcNow;
            var userId = Guid.CreateVersion7(now);
            var avatarFileId = Guid.CreateVersion7(now.AddMinutes(2));
            var wallpaperFileId = Guid.CreateVersion7(now.AddMinutes(3));
            var profileCoverFileId = Guid.CreateVersion7(now.AddMinutes(4));
            const string avatarObjectKey = "users/profile/avatar.webp";
            const string wallpaperObjectKey = "users/profile/wallpaper.webp";
            const string profileCoverObjectKey = "users/profile/profile-cover.webp";

            await using (var db = new NoCtfDbContext(options))
            {
                await db.Database.EnsureCreatedAsync(cancellationToken);
                var users = new AuthenticationStore(db, hasher);
                await users.CreateAsync(
                    userId,
                    "Player",
                    "player@example.test",
                    "eight888",
                    true,
                    now,
                    cancellationToken);
                var initialProfile = await users.GetProfileAsync(userId, cancellationToken);
                await Assert.That(initialProfile).IsNotNull();
                await users.UpdateProfileAsync(
                    userId,
                    "Persistent profile",
                    now.AddMinutes(1),
                    cancellationToken);
                db.Files.Add(new StoredFile
                {
                    Id = avatarFileId,
                    ObjectKey = avatarObjectKey,
                    FileName = "avatar.webp",
                    ContentType = "image/webp",
                    ByteLength = 4,
                    Sha256 = new byte[32],
                    CreatedAt = now.AddMinutes(2)
                });
                db.Files.Add(new StoredFile
                {
                    Id = wallpaperFileId,
                    ObjectKey = wallpaperObjectKey,
                    FileName = "wallpaper.webp",
                    ContentType = "image/webp",
                    ByteLength = 4,
                    Sha256 = new byte[32],
                    CreatedAt = now.AddMinutes(3)
                });
                db.Files.Add(new StoredFile
                {
                    Id = profileCoverFileId,
                    ObjectKey = profileCoverObjectKey,
                    FileName = "profile-cover.webp",
                    ContentType = "image/webp",
                    ByteLength = 4,
                    Sha256 = new byte[32],
                    CreatedAt = now.AddMinutes(4)
                });
                await db.SaveChangesAsync(cancellationToken);
                await users.ReplaceAvatarAsync(
                    userId,
                    avatarFileId,
                    now.AddMinutes(2),
                    cancellationToken);
                await users.ReplaceWallpaperAsync(
                    userId,
                    wallpaperFileId,
                    now.AddMinutes(3),
                    cancellationToken);
                await users.ReplaceProfileCoverAsync(
                    userId,
                    profileCoverFileId,
                    now.AddMinutes(4),
                    cancellationToken);
                await users.SetWallpaperEnabledAsync(
                    userId,
                    false,
                    now.AddMinutes(4),
                    cancellationToken);
            }

            await using (var db = new NoCtfDbContext(options))
            {
                var profile = await new AuthenticationStore(db, hasher)
                    .GetProfileAsync(userId, cancellationToken);

                await Assert.That(profile).IsNotNull();
                await Assert.That(profile!.Description).IsEqualTo("Persistent profile");
                await Assert.That(profile.AvatarFileId).IsNotNull();
                await Assert.That(profile.WallpaperFileId).IsEqualTo(wallpaperFileId);
                await Assert.That(profile.WallpaperEnabled).IsFalse();
                await Assert.That(profile.ProfileCoverFileId).IsEqualTo(profileCoverFileId);
                var storedFile = await db.Files.SingleAsync(
                    file => file.Id == profile.AvatarFileId,
                    cancellationToken);
                await Assert.That(storedFile.ObjectKey).IsEqualTo(avatarObjectKey);
            }
        });
    }

    [Test]
    [Timeout(300_000)]
    public async Task Password_change_is_atomic_and_invalidates_existing_access_and_refresh_versions(
        CancellationToken cancellationToken)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var postgres = new PostgreSqlBuilder("postgres:17.10-alpine3.24@sha256:742f40ea20b9ff2ff31db5458d127452988a2164df9e17441e191f3b72252193")
                .WithDatabase("noctf_password_change")
                .WithUsername("postgres")
                .WithPassword("postgres")
                .Build();
            await postgres.StartAsync(cancellationToken);
            var options = new DbContextOptionsBuilder<NoCtfDbContext>()
                .UseNpgsql(postgres.GetConnectionString())
                .UseSnakeCaseNamingConvention()
                .Options;
            var hasher = new PasswordHasher<User>(Options.Create(new PasswordHasherOptions
            {
                IterationCount = 10_000
            }));
            var now = DateTimeOffset.UtcNow;
            var userId = Guid.CreateVersion7(now);

            await using var db = new NoCtfDbContext(options);
            await db.Database.EnsureCreatedAsync(cancellationToken);
            var outbox = new RecordingOutbox();
            var users = new AuthenticationStore(db, hasher, outbox);
            await users.CreateAsync(
                userId,
                "PasswordOwner",
                "password-owner@example.test",
                "old-pass",
                true,
                now,
                cancellationToken);
            var before = await users.FindByIdAsync(userId, cancellationToken);

            var state = await users.ChangePasswordAsync(
                userId,
                "old-pass",
                "new-pass",
                now.AddMinutes(1),
                cancellationToken);
            var after = await users.FindByIdAsync(userId, cancellationToken);

            await Assert.That(state).IsEqualTo(ChangePasswordState.Changed);
            await Assert.That(outbox.Messages.OfType<SendPasswordChangedNotification>())
                .HasSingleItem();
            await Assert.That(after!.TokenVersion).IsEqualTo(before!.TokenVersion + 1);
            await Assert.That(await users.VerifyPasswordAsync(
                userId, "old-pass", cancellationToken)).IsFalse();
            await Assert.That(await users.VerifyPasswordAsync(
                userId, "new-pass", cancellationToken)).IsTrue();
            await Assert.That(await new AccessTokenVersionReader(db).IsCurrentAsync(
                userId, before.TokenVersion, cancellationToken)).IsFalse();

            var refresh = await new RefreshAccessToken(
                users,
                new StaleRefreshIssuer(new(userId, before.TokenVersion, MfaTestSupport.Primary(now))),
                MfaTestSupport.Unrequired(),
                TimeProvider.System)
                .ExecuteAsync("old-refresh", cancellationToken);
            await Assert.That(refresh.Succeeded).IsFalse();
            await Assert.That(refresh.FailureCode).IsEqualTo(RefreshAccessTokenFailureCode.RefreshInvalid);
        });
    }

    private sealed class StaleRefreshIssuer(RefreshTokenPrincipal principal) : IAccessTokenIssuer
    {
        public IssuedAccessToken Issue(
            AuthenticatedUser user, NoCTF.Domain.Identity.Mfa.AuthenticationContext authentication,
            DateTimeOffset now,
            TimeSpan? lifetime = null) =>
            new("unused-access", now.AddMinutes(15));

        public IssuedRefreshToken IssueRefresh(AuthenticatedUser user, NoCTF.Domain.Identity.Mfa.AuthenticationContext authentication) =>
            new("unused-refresh", DateTimeOffset.UtcNow.AddDays(30));

        public RefreshTokenPrincipal? ValidateRefresh(string token) => principal;
    }

    private static Competition Competition(
        Guid id,
        Guid ownerId,
        CompetitionAccessMode accessMode,
        string title,
        DateTimeOffset now,
        CompetitionStatus status = CompetitionStatus.Finished) => new CtfCompetition
    {
        Id = id,
        Title = title,
        OwnerId = ownerId,
        AccessMode = accessMode,
        ModeConfiguration = TestConfigurations.Competition(GameMode.Ctf),
        FlagDerivationSecret = new byte[32],
        StartAt = status == CompetitionStatus.Visible
            ? now.AddDays(2) : now.AddDays(-2),
        EndAt = status == CompetitionStatus.Visible
            ? now.AddDays(3) : now.AddDays(-1),
        Status = status,
        CreatedAt = now,
        UpdatedAt = now
    };

    private static Team Team(
        Guid id,
        Guid competitionId,
        Guid userId,
        string name,
        DateTimeOffset now) => new Team
    {
        Id = id,
        CompetitionId = competitionId,
        Name = name,
        CaptainId = userId,
        MemberIds = [userId],
        InvitationToken = id.ToString("N"),
        RegistrationStatus = TeamRegistrationStatus.Approved,
        RegisteredAt = now
    };

    private static Challenge Challenge(
        Guid id,
        Guid ownerId,
        string direction,
        DateTimeOffset now) => new CtfChallenge
    {
        Id = id,
        OwnerId = ownerId,
        Visibility = ChallengeVisibility.Shared,
        Title = $"{direction} challenge",
        Direction = direction,
        Definition = TestConfigurations.Definition(GameMode.Ctf),
        CreatedAt = now,
        UpdatedAt = now
    };

    private static CompetitionChallenge CompetitionChallenge(
        Guid id,
        Guid competitionId,
        Guid challengeId,
        DateTimeOffset now) => new CtfCompetitionChallenge
    {
        Id = id,
        CompetitionId = competitionId,
        ChallengeId = challengeId,
        IsPublished = true,
        Rules = TestConfigurations.Rules(GameMode.Ctf),
        UpdatedAt = now
    };

    private static GameplayFact SuccessfulFact(
        Guid competitionId,
        Guid competitionChallengeId,
        Guid teamId,
        Guid userId,
        DateTimeOffset occurredAt) => new FlagAttemptGameplayFact
    {
        Id = Guid.CreateVersion7(occurredAt),
        CompetitionId = competitionId,
        CompetitionChallengeId = competitionChallengeId,
        TeamId = teamId,
        ActorUserId = userId,
        OccurredAt = occurredAt,
        Value = "flag{profile}",
        ValueSha256 = new byte[32],
        State = GameplayFactState.Completed,
        Result = GameplayFactResult.Correct,
        UpdatedAt = occurredAt
    };

    private sealed class RecordingOutbox : IPostCommitMessagePublisher
    {
        public List<object> Messages { get; } = [];

        public ValueTask PublishAsync<T>(T message)
        {
            Messages.Add(message!);
            return ValueTask.CompletedTask;
        }

        public ValueTask ScheduleAsync<T>(T message, DateTimeOffset scheduledAt) =>
            ValueTask.CompletedTask;

        public ValueTask PublishToRunnerNodeAsync<T>(T message)
            where T : IRunnerNodeMessage => ValueTask.CompletedTask;

        public ValueTask ScheduleToRunnerNodeAsync<T>(
            T message,
            DateTimeOffset scheduledAt)
            where T : IRunnerNodeMessage => ValueTask.CompletedTask;

        public Task FlushOutgoingMessagesAsync() => Task.CompletedTask;
    }
}
