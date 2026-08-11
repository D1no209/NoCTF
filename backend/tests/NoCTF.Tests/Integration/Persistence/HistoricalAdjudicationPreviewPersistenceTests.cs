using System.Text.Json;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using NoCTF.Application.GameplayFacts.AdjudicationPreview;
using NoCTF.Domain.Challenges;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Competitions.Events;
using NoCTF.Domain.Gameplay;
using NoCTF.Domain.Identity;
using NoCTF.Domain.Shared;
using NoCTF.Domain.Teams;
using NoCTF.Infrastructure.GameplayFacts.AdjudicationPreview;
using NoCTF.Infrastructure.Persistence;
using NoCTF.Infrastructure.Teams.Moderation;
using Testcontainers.PostgreSql;

namespace NoCTF.Tests.Integration.Persistence;

[Category("Integration")]
public sealed class HistoricalAdjudicationPreviewPersistenceTests
{
    [Test]
    [Timeout(300_000)]
    public async Task Preview_detects_out_of_order_and_duplicate_blood_without_writing(
        CancellationToken cancellationToken)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var postgres = await StartPostgresAsync(cancellationToken);
            var options = Options(postgres);
            var fixture = await SeedAsync(options, cancellationToken);

            await using (var initial = new NoCtfDbContext(options))
            {
                var initialPreview = new PreviewHistoricalAdjudicationDifferences(
                    new HistoricalAdjudicationPreviewStore(initial));
                var normal = await initialPreview.ExecuteAsync(
                    fixture.CompetitionId, null, null, null, 20, cancellationToken);
                await Assert.That(normal.Items).IsEmpty();
            }

            await SeedHistoricalDifferencesAsync(options, fixture, cancellationToken);
            await using var db = new NoCtfDbContext(options);
            var authorizer = new CompetitionModerationAuthorizer(db);
            foreach (var staffId in new[]
                     {
                         fixture.OwnerId,
                         fixture.ManagerId,
                         fixture.JudgeId,
                         fixture.ObserverId,
                         fixture.AdministratorId
                     })
            {
                await Assert.That(await authorizer.CanObserveAsync(
                    staffId, fixture.CompetitionId, cancellationToken)).IsTrue();
            }
            await Assert.That(await authorizer.CanObserveAsync(
                fixture.FirstUserId, fixture.CompetitionId, cancellationToken)).IsFalse();
            var before = await CountsAsync(db, cancellationToken);
            var preview = new PreviewHistoricalAdjudicationDifferences(
                new HistoricalAdjudicationPreviewStore(db));

            var page = await preview.ExecuteAsync(
                fixture.CompetitionId, null, null, null, 20, cancellationToken);

            var after = await CountsAsync(db, cancellationToken);
            await Assert.That(after).IsEqualTo(before);
            var later = page.Items.Single(item => item.GameplayFactId == fixture.LaterFactId);
            await Assert.That(later.Differences.Select(item => item.Kind))
                .Contains(AdjudicationDifferenceKind.CurrentCorrectShouldBeDuplicate);
            await Assert.That(later.Differences.Select(item => item.Kind))
                .Contains(AdjudicationDifferenceKind.DuplicateBloodAward);
            await Assert.That(later.Differences.Select(item => item.Kind))
                .Contains(AdjudicationDifferenceKind.HistoricalResultChanged);
            await Assert.That(later.Differences
                .Single(item => item.Kind == AdjudicationDifferenceKind.HistoricalResultChanged)
                .Certainty).IsEqualTo(AdjudicationDifferenceCertainty.NeedsReview);
            await Assert.That(page.Items.Single(item => item.GameplayFactId == fixture.EarlierFactId)
                .Differences.Select(item => item.Kind))
                .Contains(AdjudicationDifferenceKind.MissingBloodAward);

            var firstPage = await preview.ExecuteAsync(
                fixture.CompetitionId, null, null, null, 1, cancellationToken);
            await Assert.That(firstPage.Items).Count().IsEqualTo(1);
            await Assert.That(firstPage.NextBeforeOccurredAt).IsNotNull();
            await Assert.That(firstPage.NextBeforeId).IsNotNull();
            var secondPage = await preview.ExecuteAsync(
                fixture.CompetitionId,
                null,
                firstPage.NextBeforeOccurredAt,
                firstPage.NextBeforeId,
                1,
                cancellationToken);
            await Assert.That(secondPage.Items).Count().IsEqualTo(1);
            await Assert.That(firstPage.Items.Concat(secondPage.Items)
                .Select(item => item.GameplayFactId)
                .Distinct()).Count().IsEqualTo(2);

            await db.Competitions.Where(item => item.Id == fixture.CompetitionId)
                .ExecuteUpdateAsync(setters => setters.SetProperty(item => item.Mode, GameMode.Awdp),
                    cancellationToken);
            await db.GameplayFacts.Where(item => item.CompetitionId == fixture.CompetitionId)
                .ExecuteUpdateAsync(setters => setters.SetProperty(
                    item => item.Kind, GameplayFactKind.BreakAttempt), cancellationToken);
            await db.CompetitionEvents.Where(item => item.CompetitionId == fixture.CompetitionId
                    && (item.Kind == CompetitionEventKind.FirstBloodAwarded
                        || item.Kind == CompetitionEventKind.SecondBloodAwarded
                        || item.Kind == CompetitionEventKind.ThirdBloodAwarded))
                .ExecuteDeleteAsync(cancellationToken);
            var breakPreview = await preview.ExecuteAsync(
                fixture.CompetitionId, null, null, null, 20, cancellationToken);
            await Assert.That(breakPreview.Items
                .Single(item => item.GameplayFactId == fixture.LaterFactId)
                .Differences.Select(item => item.Kind))
                .Contains(AdjudicationDifferenceKind.CurrentCorrectShouldBeDuplicate);
        });
    }

    private static async Task SeedHistoricalDifferencesAsync(
        DbContextOptions<NoCtfDbContext> options,
        Fixture fixture,
        CancellationToken ct)
    {
        await using var db = new NoCtfDbContext(options);
        db.GameplayFacts.Add(new GameplayFact
        {
            Id = fixture.EarlierFactId,
            CompetitionId = fixture.CompetitionId,
            CompetitionChallengeId = fixture.CompetitionChallengeId,
            TeamId = fixture.FirstTeamId,
            ActorUserId = fixture.FirstUserId,
            Kind = GameplayFactKind.FlagAttempt,
            Value = "flag{preview}",
            ValueSha256 = SHA256.HashData(Encoding.UTF8.GetBytes("flag{preview}")),
            State = GameplayFactState.Completed,
            Result = GameplayFactResult.Correct,
            OccurredAt = fixture.Now,
            UpdatedAt = fixture.Now
        });
        db.CompetitionEvents.AddRange(
            Event(fixture, fixture.LaterFactId, CompetitionEventKind.FirstBloodAwarded,
                fixture.Now.AddSeconds(1), GameplayFactResult.Correct),
            Event(fixture, fixture.LaterFactId, CompetitionEventKind.GameplayFactAdjudicated,
                fixture.Now.AddSeconds(2), GameplayFactResult.Wrong));
        await db.SaveChangesAsync(ct);
    }

    private static CompetitionEvent Event(
        Fixture fixture,
        Guid factId,
        CompetitionEventKind kind,
        DateTimeOffset occurredAt,
        GameplayFactResult result) => new()
    {
        Id = Guid.CreateVersion7(occurredAt),
        CompetitionId = fixture.CompetitionId,
        Kind = kind,
        Level = CompetitionEventLevel.Information,
        Visibility = CompetitionEventVisibility.Staff,
        SubjectType = EntityReferenceKind.GameplayFact,
        SubjectId = factId,
        RelatedType = EntityReferenceKind.Team,
        RelatedId = fixture.FirstTeamId,
        PayloadJson = JsonSerializer.Serialize(new
        {
            schemaVersion = 1,
            gameplayFactResult = result.ToString(),
            competitionChallengeId = fixture.CompetitionChallengeId
        }),
        OccurredAt = occurredAt
    };

    private static async Task<Fixture> SeedAsync(
        DbContextOptions<NoCtfDbContext> options,
        CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow.AddMinutes(-10);
        var ownerId = Guid.CreateVersion7();
        var userId = Guid.CreateVersion7();
        var managerId = Guid.CreateVersion7();
        var judgeId = Guid.CreateVersion7();
        var observerId = Guid.CreateVersion7();
        var administratorId = Guid.CreateVersion7();
        var competitionId = Guid.CreateVersion7();
        var challengeId = Guid.CreateVersion7();
        var competitionChallengeId = Guid.CreateVersion7();
        var teamId = Guid.CreateVersion7();
        var fixture = new Fixture(
            now,
            competitionId,
            competitionChallengeId,
            teamId,
            userId,
            ownerId,
            managerId,
            judgeId,
            observerId,
            administratorId,
            Guid.Parse("10000000-0000-0000-0000-000000000001"),
            Guid.Parse("20000000-0000-0000-0000-000000000001"));
        await using var db = new NoCtfDbContext(options);
        await db.Database.MigrateAsync(ct);
        db.Users.AddRange(
            User(ownerId, "preview-owner", UserRole.Organizer, now),
            User(userId, "preview-player", UserRole.User, now),
            User(administratorId, "preview-admin", UserRole.Administrator, now));
        db.Competitions.Add(new Competition
        {
            Id = competitionId,
            OwnerId = ownerId,
            ManagerIds = [managerId],
            JudgeIds = [judgeId],
            ObserverIds = [observerId],
            Title = "Adjudication preview",
            Mode = GameMode.Ctf,
            ConfigurationUpdatedAt = now,
            FlagDerivationSecret = new byte[32],
            StartAt = now,
            EndAt = now.AddHours(2),
            Status = CompetitionStatus.Running,
            CreatedAt = now,
            UpdatedAt = now
        });
        db.Teams.Add(new Team
        {
            Id = teamId,
            CompetitionId = competitionId,
            Name = "Preview Team",
            NormalizedName = "PREVIEW TEAM",
            CaptainId = userId,
            MemberIds = [userId],
            InvitationToken = new string('a', 32),
            RegistrationStatus = TeamRegistrationStatus.Approved,
            RegisteredAt = now
        });
        db.Challenges.Add(new Challenge
        {
            Id = challengeId,
            OwnerId = ownerId,
            Mode = GameMode.Ctf,
            Title = "Preview challenge",
            Direction = "Web",
            CreatedAt = now,
            UpdatedAt = now
        });
        db.CompetitionChallenges.Add(new CompetitionChallenge
        {
            Id = competitionChallengeId,
            CompetitionId = competitionId,
            ChallengeId = challengeId,
            BaseScore = 100,
            IsPublished = true,
            UpdatedAt = now
        });
        db.GameplayFacts.Add(new GameplayFact
        {
            Id = fixture.LaterFactId,
            CompetitionId = competitionId,
            CompetitionChallengeId = competitionChallengeId,
            TeamId = teamId,
            ActorUserId = userId,
            Kind = GameplayFactKind.FlagAttempt,
            Value = "flag{preview}",
            ValueSha256 = SHA256.HashData(Encoding.UTF8.GetBytes("flag{preview}")),
            State = GameplayFactState.Completed,
            Result = GameplayFactResult.Correct,
            OccurredAt = now.AddSeconds(1),
            UpdatedAt = now.AddSeconds(1)
        });
        db.CompetitionEvents.AddRange(
            Event(fixture, fixture.LaterFactId, CompetitionEventKind.FirstBloodAwarded,
                now.AddSeconds(1), GameplayFactResult.Correct),
            Event(fixture, fixture.LaterFactId, CompetitionEventKind.GameplayFactAdjudicated,
                now.AddSeconds(1), GameplayFactResult.Correct));
        await db.SaveChangesAsync(ct);
        return fixture;
    }

    private static User User(Guid id, string name, UserRole role, DateTimeOffset now) => new()
    {
        Id = id,
        UserName = name,
        NormalizedUserName = name.ToUpperInvariant(),
        Email = $"{name}@example.test",
        NormalizedEmail = $"{name.ToUpperInvariant()}@EXAMPLE.TEST",
        PasswordHash = "test",
        Kind = UserKind.Human,
        Role = role,
        EmailVerifiedAt = now,
        CreatedAt = now,
        UpdatedAt = now
    };

    private static async Task<(int Facts, int Events, int Notifications)> CountsAsync(
        NoCtfDbContext db,
        CancellationToken ct) =>
        (await db.GameplayFacts.CountAsync(ct),
            await db.CompetitionEvents.CountAsync(ct),
            await db.Notifications.CountAsync(ct));

    private static async Task<PostgreSqlContainer> StartPostgresAsync(CancellationToken ct)
    {
        var postgres = new PostgreSqlBuilder("postgres:17-alpine")
            .WithDatabase("noctf_adjudication_preview")
            .WithUsername("postgres")
            .WithPassword("postgres")
            .Build();
        await postgres.StartAsync(ct);
        return postgres;
    }

    private static DbContextOptions<NoCtfDbContext> Options(PostgreSqlContainer postgres) =>
        new DbContextOptionsBuilder<NoCtfDbContext>()
            .UseNpgsql(postgres.GetConnectionString())
            .UseSnakeCaseNamingConvention()
            .Options;

    private sealed record Fixture(
        DateTimeOffset Now,
        Guid CompetitionId,
        Guid CompetitionChallengeId,
        Guid FirstTeamId,
        Guid FirstUserId,
        Guid OwnerId,
        Guid ManagerId,
        Guid JudgeId,
        Guid ObserverId,
        Guid AdministratorId,
        Guid EarlierFactId,
        Guid LaterFactId);
}
