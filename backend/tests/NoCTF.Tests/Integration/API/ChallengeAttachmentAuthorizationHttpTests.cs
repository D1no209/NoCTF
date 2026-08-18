using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using FastEndpoints;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NoCTF.API.Composition;
using NoCTF.API.Endpoints.Challenges;
using NoCTF.API.Security;
using NoCTF.Application.Challenges.Attachments;
using NoCTF.Application.Storage;
using NoCTF.Domain.Challenges;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Identity;
using NoCTF.Domain.Storage;
using NoCTF.Domain.Teams;
using NoCTF.Infrastructure.Challenges.Attachments;
using NoCTF.Infrastructure.Persistence;
using Testcontainers.PostgreSql;

namespace NoCTF.Tests.Integration.API;

[Category("Integration")]
public sealed class ChallengeAttachmentAuthorizationHttpTests
{
    private const string BearerScheme = "Bearer";

    [Test]
    [Timeout(300_000)]
    public async Task Player_downloads_are_bound_to_the_authorized_challenge_scope(
        CancellationToken cancellationToken)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var postgres = new PostgreSqlBuilder("postgres:17.10-alpine3.24@sha256:742f40ea20b9ff2ff31db5458d127452988a2164df9e17441e191f3b72252193")
                .WithDatabase("noctf_attachment_authorization")
                .WithUsername("postgres")
                .WithPassword("postgres")
                .Build();
            await postgres.StartAsync(cancellationToken);
            var fixture = await SeedAsync(postgres.GetConnectionString(), cancellationToken);
            await using var app = await CreateApplicationAsync(
                postgres.GetConnectionString(),
                fixture,
                cancellationToken);
            using var client = app.GetTestClient();
            client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue(BearerScheme, "test-token");

            using var allList = await client.GetAsync(
                AttachmentListUri(fixture.CompetitionId, fixture.AllCompetitionChallengeId),
                cancellationToken);
            using var allListJson = JsonDocument.Parse(await allList.Content.ReadAsStringAsync(cancellationToken));
            await Assert.That(allList.StatusCode).IsEqualTo(HttpStatusCode.OK);
            await Assert.That(allListJson.RootElement.GetProperty("deliveryPolicy").GetString()).IsEqualTo("All");
            await Assert.That(allListJson.RootElement.GetProperty("items").GetArrayLength()).IsEqualTo(2);

            using var randomList = await client.GetAsync(
                AttachmentListUri(fixture.CompetitionId, fixture.RandomCompetitionChallengeId),
                cancellationToken);
            using var randomListJson = JsonDocument.Parse(await randomList.Content.ReadAsStringAsync(cancellationToken));
            await Assert.That(randomList.StatusCode).IsEqualTo(HttpStatusCode.OK);
            await Assert.That(randomListJson.RootElement.GetProperty("deliveryPolicy").GetString())
                .IsEqualTo("RandomOnePerTeam");
            await Assert.That(randomListJson.RootElement.GetProperty("items").GetArrayLength()).IsEqualTo(0);

            using var sameChallenge = await client.GetAsync(
                AttachmentUri(
                    fixture.CompetitionId,
                    fixture.AllCompetitionChallengeId,
                    fixture.AllAttachmentId),
                cancellationToken);
            await Assert.That(sameChallenge.StatusCode).IsEqualTo(HttpStatusCode.OK);
            await Assert.That(await sameChallenge.Content.ReadAsStringAsync(cancellationToken))
                .IsEqualTo("all-scope");

            using var crossChallenge = await client.GetAsync(
                AttachmentUri(
                    fixture.CompetitionId,
                    fixture.AllCompetitionChallengeId,
                    fixture.OtherAttachmentId),
                cancellationToken);
            await Assert.That(crossChallenge.StatusCode).IsEqualTo(HttpStatusCode.NotFound);

            using var deleted = await client.GetAsync(
                AttachmentUri(
                    fixture.CompetitionId,
                    fixture.AllCompetitionChallengeId,
                    fixture.DeletedAttachmentId),
                cancellationToken);
            await Assert.That(deleted.StatusCode).IsEqualTo(HttpStatusCode.NotFound);

            using var unpublished = await client.GetAsync(
                AttachmentUri(
                    fixture.CompetitionId,
                    fixture.UnpublishedCompetitionChallengeId,
                    fixture.OtherAttachmentId),
                cancellationToken);
            await Assert.That(unpublished.StatusCode).IsEqualTo(HttpStatusCode.NotFound);

            using var randomFromAll = await client.GetAsync(
                RandomAttachmentUri(
                    fixture.CompetitionId,
                    fixture.AllCompetitionChallengeId),
                cancellationToken);
            await Assert.That(randomFromAll.StatusCode).IsEqualTo(HttpStatusCode.NotFound);

            using var randomById = await client.GetAsync(
                AttachmentUri(
                    fixture.CompetitionId,
                    fixture.RandomCompetitionChallengeId,
                    fixture.RandomAttachmentId),
                cancellationToken);
            await Assert.That(randomById.StatusCode).IsEqualTo(HttpStatusCode.NotFound);

            using var randomSameChallenge = await client.GetAsync(
                RandomAttachmentUri(
                    fixture.CompetitionId,
                    fixture.RandomCompetitionChallengeId),
                cancellationToken);
            await Assert.That(randomSameChallenge.StatusCode).IsEqualTo(HttpStatusCode.OK);
            await Assert.That(await randomSameChallenge.Content.ReadAsStringAsync(cancellationToken))
                .IsEqualTo("random-scope");
            var disposition = randomSameChallenge.Content.Headers.ContentDisposition?.ToString() ?? string.Empty;
            await Assert.That(disposition).Contains("challenge.zip");
            await Assert.That(disposition).DoesNotContain("flag{random}");

            using var randomCrossChallenge = await client.GetAsync(
                RandomAttachmentUri(
                    fixture.CompetitionId,
                    fixture.CrossRandomCompetitionChallengeId),
                cancellationToken);
            await Assert.That(randomCrossChallenge.StatusCode).IsEqualTo(HttpStatusCode.NotFound);

            await using var verification = CreateDbContext(
                postgres.GetConnectionString());
            await Assert.That(await verification.ChallengeFlags.CountAsync(flag =>
                    flag.CompetitionChallengeId == fixture.RandomCompetitionChallengeId &&
                    flag.TeamId == fixture.TeamId &&
                    flag.SpecificationKind == SpecificationKind.Attachment,
                cancellationToken)).IsEqualTo(1);
            await Assert.That(await verification.ChallengeFlags.AnyAsync(flag =>
                    flag.CompetitionChallengeId == fixture.CrossRandomCompetitionChallengeId &&
                    flag.TeamId == fixture.TeamId &&
                    flag.SpecificationKind == SpecificationKind.Attachment,
                cancellationToken)).IsFalse();
        });
    }

    private static string AttachmentUri(
        Guid competitionId,
        Guid competitionChallengeId,
        Guid attachmentId) =>
        $"/api/v1/competitions/{competitionId}/challenges/{competitionChallengeId}/attachments/{attachmentId}";

    private static string AttachmentListUri(
        Guid competitionId,
        Guid competitionChallengeId) =>
        $"/api/v1/competitions/{competitionId}/challenges/{competitionChallengeId}/attachments";

    private static string RandomAttachmentUri(
        Guid competitionId,
        Guid competitionChallengeId) =>
        $"/api/v1/competitions/{competitionId}/challenges/{competitionChallengeId}/attachment";

    private static async Task<WebApplication> CreateApplicationAsync(
        string connectionString,
        Fixture fixture,
        CancellationToken cancellationToken)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddProblemDetails();
        builder.Services.AddFastEndpoints(options =>
        {
            options.DisableAutoDiscovery = true;
            options.Assemblies = [typeof(DownloadChallengeAttachmentEndpoint).Assembly];
            options.Filter = type =>
                type == typeof(ListChallengeAttachmentsEndpoint)
                || type == typeof(DownloadChallengeAttachmentEndpoint)
                || type == typeof(DownloadRandomChallengeAttachmentEndpoint);
        });
        builder.Services
            .AddAuthentication(options =>
            {
                options.DefaultAuthenticateScheme = BearerScheme;
                options.DefaultChallengeScheme = BearerScheme;
            })
            .AddScheme<AuthenticationSchemeOptions, TestBearerHandler>(BearerScheme, _ => { });
        builder.Services.AddAuthorization();
        builder.Services.AddDbContext<NoCtfDbContext>(options => options
            .UseNpgsql(connectionString)
            .UseSnakeCaseNamingConvention());
        builder.Services.AddScoped<IChallengeAttachmentStore>(serviceProvider =>
            new ChallengeAttachmentStore(serviceProvider.GetRequiredService<NoCtfDbContext>()));
        builder.Services.AddScoped<GetChallengeAttachments>();
        builder.Services.AddSingleton<IUserContext>(new TestUserContext(fixture.UserId));
        builder.Services.AddSingleton<IObjectStorage>(new TestObjectStorage(fixture.Objects));

        var app = builder.Build();
        app.UseAuthentication();
        app.UseAuthorization();
        app.UseNoCtfEndpoints();
        await app.StartAsync(cancellationToken);
        return app;
    }

    private static async Task<Fixture> SeedAsync(
        string connectionString,
        CancellationToken cancellationToken)
    {
        await using var db = CreateDbContext(connectionString);
        await db.Database.MigrateAsync(cancellationToken);
        var now = DateTimeOffset.UtcNow;
        var userId = Guid.CreateVersion7();
        var competitionId = Guid.CreateVersion7();
        var teamId = Guid.CreateVersion7();
        var allChallengeId = Guid.CreateVersion7();
        var otherChallengeId = Guid.CreateVersion7();
        var randomChallengeId = Guid.CreateVersion7();
        var crossRandomChallengeId = Guid.CreateVersion7();
        var allCompetitionChallengeId = Guid.CreateVersion7();
        var unpublishedCompetitionChallengeId = Guid.CreateVersion7();
        var randomCompetitionChallengeId = Guid.CreateVersion7();
        var crossRandomCompetitionChallengeId = Guid.CreateVersion7();
        var allAttachmentId = Guid.CreateVersion7();
        var allSecondAttachmentId = Guid.CreateVersion7();
        var otherAttachmentId = Guid.CreateVersion7();
        var randomAttachmentId = Guid.CreateVersion7();
        var deletedAttachmentId = Guid.CreateVersion7();
        var allFileId = Guid.CreateVersion7();
        var allSecondFileId = Guid.CreateVersion7();
        var otherFileId = Guid.CreateVersion7();
        var randomFileId = Guid.CreateVersion7();
        var deletedFileId = Guid.CreateVersion7();
        var objects = new Dictionary<string, byte[]>
        {
            ["attachments/all"] = Encoding.UTF8.GetBytes("all-scope"),
            ["attachments/all-second"] = Encoding.UTF8.GetBytes("all-second-scope"),
            ["attachments/other"] = Encoding.UTF8.GetBytes("other-scope"),
            ["attachments/random"] = Encoding.UTF8.GetBytes("random-scope"),
            ["attachments/deleted"] = Encoding.UTF8.GetBytes("deleted-scope")
        };

        db.Users.Add(new User
        {
            Id = userId,
            UserName = "participant",
            NormalizedUserName = "PARTICIPANT",
            Email = "participant@example.test",
            NormalizedEmail = "PARTICIPANT@EXAMPLE.TEST",
            PasswordHash = "test",
            CreatedAt = now,
            UpdatedAt = now
        });
        db.Competitions.Add(new Competition
        {
            Id = competitionId,
            OwnerId = userId,
            Title = "Attachment authorization",
            Mode = GameMode.Ctf,
            Status = CompetitionStatus.Running,
            ConfigurationJson = "{}",
            FlagDerivationSecret = new byte[32],
            StartAt = now.AddHours(-1),
            EndAt = now.AddHours(1),
            RunningSince = now.AddHours(-1),
            ConfigurationUpdatedAt = now,
            CreatedAt = now,
            UpdatedAt = now
        });
        db.Teams.Add(new Team
        {
            Id = teamId,
            CompetitionId = competitionId,
            Name = "participant-team",
            NormalizedName = "PARTICIPANT-TEAM",
            CaptainId = userId,
            MemberIds = [userId],
            InvitationToken = "12345678901234567890123456789012",
            RegistrationStatus = TeamRegistrationStatus.Approved,
            RegisteredAt = now
        });
        db.Challenges.AddRange(
            Challenge(allChallengeId, userId, "All"),
            Challenge(otherChallengeId, userId, "Other"),
            Challenge(randomChallengeId, userId, "Random"),
            Challenge(crossRandomChallengeId, userId, "Cross Random"));
        db.CompetitionChallenges.AddRange(
            CompetitionChallenge(
                allCompetitionChallengeId,
                competitionId,
                allChallengeId,
                isPublished: true,
                order: 1,
                "All",
                now),
            CompetitionChallenge(
                unpublishedCompetitionChallengeId,
                competitionId,
                otherChallengeId,
                isPublished: false,
                order: 2,
                "All",
                now),
            CompetitionChallenge(
                randomCompetitionChallengeId,
                competitionId,
                randomChallengeId,
                isPublished: true,
                order: 3,
                "RandomOnePerTeam",
                now),
            CompetitionChallenge(
                crossRandomCompetitionChallengeId,
                competitionId,
                crossRandomChallengeId,
                isPublished: true,
                order: 4,
                "RandomOnePerTeam",
                now));
        db.Files.AddRange(
            File(allFileId, "attachments/all", "all.txt", objects["attachments/all"], now),
            File(allSecondFileId, "attachments/all-second", "all-second.txt", objects["attachments/all-second"], now),
            File(otherFileId, "attachments/other", "other.txt", objects["attachments/other"], now),
            File(randomFileId, "attachments/random", "challenge.zip", objects["attachments/random"], now),
            File(deletedFileId, "attachments/deleted", "deleted.txt", objects["attachments/deleted"], now));
        db.Set<ChallengeAttachment>().AddRange(
            Attachment(allAttachmentId, allChallengeId, allFileId, now),
            Attachment(allSecondAttachmentId, allChallengeId, allSecondFileId, now),
            Attachment(otherAttachmentId, otherChallengeId, otherFileId, now),
            Attachment(randomAttachmentId, randomChallengeId, randomFileId, now),
            Attachment(deletedAttachmentId, allChallengeId, deletedFileId, now, now));
        db.ChallengeFlags.AddRange(
            AttachmentCandidate(randomChallengeId, randomAttachmentId, "flag{random}", now),
            AttachmentCandidate(crossRandomChallengeId, otherAttachmentId, "flag{cross}", now));
        await db.SaveChangesAsync(cancellationToken);

        return new(
            userId,
            competitionId,
            teamId,
            allCompetitionChallengeId,
            unpublishedCompetitionChallengeId,
            randomCompetitionChallengeId,
            crossRandomCompetitionChallengeId,
            allAttachmentId,
            otherAttachmentId,
            randomAttachmentId,
            deletedAttachmentId,
            objects);
    }

    private static NoCtfDbContext CreateDbContext(string connectionString) =>
        new(new DbContextOptionsBuilder<NoCtfDbContext>()
            .UseNpgsql(connectionString)
            .UseSnakeCaseNamingConvention()
            .Options);

    private static Challenge Challenge(Guid id, Guid ownerId, string title) => new()
    {
        Id = id,
        OwnerId = ownerId,
        Title = title,
        Mode = GameMode.Ctf,
        DefinitionJson = "{}",
        CreatedAt = DateTimeOffset.UtcNow,
        UpdatedAt = DateTimeOffset.UtcNow
    };

    private static CompetitionChallenge CompetitionChallenge(
        Guid id,
        Guid competitionId,
        Guid challengeId,
        bool isPublished,
        int order,
        string policy,
        DateTimeOffset now) => new()
        {
            Id = id,
            CompetitionId = competitionId,
            ChallengeId = challengeId,
            BaseScore = 500,
            Order = order,
            IsPublished = isPublished,
            RulesJson = JsonSerializer.Serialize(new
            {
                schemaVersion = 1,
                attachmentPolicy = policy
            }),
            UpdatedAt = now
        };

    private static StoredFile File(
        Guid id,
        string objectKey,
        string fileName,
        byte[] content,
        DateTimeOffset now) => new()
        {
            Id = id,
            ObjectKey = objectKey,
            FileName = fileName,
            ContentType = "text/plain",
            ByteLength = content.Length,
            Sha256 = new byte[32],
            CreatedAt = now
        };

    private static ChallengeAttachment Attachment(
        Guid id,
        Guid challengeId,
        Guid fileId,
        DateTimeOffset now,
        DateTimeOffset? deletedAt = null) => new()
        {
            Id = id,
            ChallengeId = challengeId,
            FileId = fileId,
            CreatedAt = now,
            DeletedAt = deletedAt
        };

    private static ChallengeFlag AttachmentCandidate(
        Guid challengeId,
        Guid attachmentId,
        string flag,
        DateTimeOffset now) => new()
        {
            Id = Guid.CreateVersion7(),
            ChallengeId = challengeId,
            Flag = flag,
            FlagSha256 = new byte[32],
            SpecificationKind = SpecificationKind.Attachment,
            SpecificationId = attachmentId,
            CreatedAt = now
        };

    private sealed record Fixture(
        Guid UserId,
        Guid CompetitionId,
        Guid TeamId,
        Guid AllCompetitionChallengeId,
        Guid UnpublishedCompetitionChallengeId,
        Guid RandomCompetitionChallengeId,
        Guid CrossRandomCompetitionChallengeId,
        Guid AllAttachmentId,
        Guid OtherAttachmentId,
        Guid RandomAttachmentId,
        Guid DeletedAttachmentId,
        IReadOnlyDictionary<string, byte[]> Objects);

    private sealed class TestUserContext(Guid userId) : IUserContext
    {
        public Guid UserId { get; } = userId;
        public bool IsAdministrator => false;
    }

    private sealed class TestObjectStorage(IReadOnlyDictionary<string, byte[]> objects)
        : IObjectStorage
    {
        public Task<StoredObject?> InspectAsync(
            string objectKey,
            CancellationToken cancellationToken)
        {
            if (!objects.TryGetValue(objectKey, out var content))
                return Task.FromResult<StoredObject?>(null);
            return Task.FromResult<StoredObject?>(new(
                objectKey,
                objectKey,
                "application/octet-stream",
                content.LongLength,
                string.Empty));
        }

        public Task<StoredObject> PutAsync(
            string objectKey,
            string fileName,
            string contentType,
            Stream content,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<Stream> OpenReadAsync(
            string objectKey,
            CancellationToken cancellationToken) =>
            Task.FromResult<Stream>(new MemoryStream(objects[objectKey], writable: false));

        public Task DeleteAsync(
            string objectKey,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class TestBearerHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var identity = new ClaimsIdentity(
                [new Claim(ClaimTypes.NameIdentifier, "test-user")],
                Scheme.Name);
            return Task.FromResult(AuthenticateResult.Success(
                new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name)));
        }
    }
}
