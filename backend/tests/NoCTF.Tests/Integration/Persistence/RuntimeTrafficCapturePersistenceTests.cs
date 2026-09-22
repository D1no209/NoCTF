using FluentStorage.Storage;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using NoCTF.Application.Competitions.Events;
using NoCTF.Application.Messaging;
using NoCTF.Application.Runtime.Access;
using NoCTF.Domain.Challenges;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Competitions.Events;
using NoCTF.Domain.Identity;
using NoCTF.Domain.Runtime;
using NoCTF.Domain.Shared;
using NoCTF.Domain.Storage;
using NoCTF.Infrastructure.Persistence;
using NoCTF.Infrastructure.Runtime.Access;
using Testcontainers.PostgreSql;

namespace NoCTF.Tests.Integration.Persistence;

[Category("Integration")]
public sealed class RuntimeTrafficCapturePersistenceTests
{
    [Test]
    [Timeout(300_000)]
    public async Task List_groups_segments_and_filters_truncated_captures_in_PostgreSQL(
        CancellationToken cancellationToken)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var postgres = new PostgreSqlBuilder(
                    "postgres:17.10-alpine3.24@sha256:742f40ea20b9ff2ff31db5458d127452988a2164df9e17441e191f3b72252193")
                .WithDatabase("noctf_runtime_traffic_captures")
                .WithUsername("postgres")
                .WithPassword("postgres")
                .Build();
            await postgres.StartAsync(cancellationToken);
            var options = new DbContextOptionsBuilder<NoCtfDbContext>()
                .UseNpgsql(postgres.GetConnectionString())
                .UseSnakeCaseNamingConvention()
                .Options;
            var now = DateTimeOffset.UtcNow;
            var ownerId = Guid.CreateVersion7();
            var competitionId = Guid.CreateVersion7();
            var challengeId = Guid.CreateVersion7();
            var competitionChallengeId = Guid.CreateVersion7();
            var truncatedRuntimeId = Guid.CreateVersion7();
            var completeRuntimeId = Guid.CreateVersion7();

            await using var db = new NoCtfDbContext(options);
            await db.Database.EnsureCreatedAsync(cancellationToken);
            db.Users.Add(new User
            {
                Id = ownerId,
                UserName = "capture-owner",
                NormalizedUserName = "CAPTURE-OWNER",
                Email = "capture-owner@example.test",
                PasswordHash = "test",
                Role = UserRole.Administrator,
                CreatedAt = now,
                UpdatedAt = now
            });
            db.Competitions.Add(new Competition
            {
                Id = competitionId,
                OwnerId = ownerId,
                Title = "Traffic captures",
                Mode = GameMode.Ctf,
                StartAt = now.AddHours(-1),
                EndAt = now.AddHours(1),
                Status = CompetitionStatus.Running,
                MaxTeamMembers = 5,
                ConfigurationJson = "{}",
                FlagDerivationSecret = new byte[32],
                CreatedAt = now,
                UpdatedAt = now
            });
            db.Challenges.Add(new Challenge
            {
                Id = challengeId,
                OwnerId = ownerId,
                Mode = GameMode.Ctf,
                Title = "Capture target",
                DefinitionJson = "{}",
                CreatedAt = now,
                UpdatedAt = now
            });
            db.CompetitionChallenges.Add(new CompetitionChallenge
            {
                Id = competitionChallengeId,
                CompetitionId = competitionId,
                ChallengeId = challengeId,
                Order = 1,
                IsPublished = true,
                RulesJson = "{}",
                UpdatedAt = now
            });
            db.RuntimeInstances.AddRange(
                Runtime(truncatedRuntimeId, competitionId, competitionChallengeId, now),
                Runtime(completeRuntimeId, competitionId, competitionChallengeId, now));

            AddSegment(
                db,
                competitionId,
                competitionChallengeId,
                truncatedRuntimeId,
                now,
                byteLength: 120,
                truncated: false);
            AddSegment(
                db,
                competitionId,
                competitionChallengeId,
                truncatedRuntimeId,
                now.AddSeconds(1),
                byteLength: 80,
                truncated: true);
            AddSegment(
                db,
                competitionId,
                competitionChallengeId,
                completeRuntimeId,
                now.AddSeconds(2),
                byteLength: 40,
                truncated: false);
            await db.SaveChangesAsync(cancellationToken);

            var store = new RuntimeTrafficCaptureStore(
                db,
                Substitute.For<IStore>(),
                Substitute.For<ICompetitionEventRecorder>(),
                Substitute.For<ITransactionalMessageOutbox>(),
                TimeProvider.System);
            var all = await store.ListAsync(
                Query(competitionId, truncated: null),
                cancellationToken);
            var truncated = await store.ListAsync(
                Query(competitionId, truncated: true),
                cancellationToken);
            var complete = await store.ListAsync(
                Query(competitionId, truncated: false),
                cancellationToken);

            await Assert.That(all.Total).IsEqualTo(2);
            await Assert.That(all.Items).Count().IsEqualTo(2);
            var aggregated = all.Items.Single(item =>
                item.RuntimeInstanceId == truncatedRuntimeId);
            await Assert.That(aggregated.SegmentCount).IsEqualTo(2);
            await Assert.That(aggregated.ByteLength).IsEqualTo(200);
            await Assert.That(aggregated.Truncated).IsTrue();
            await Assert.That(aggregated.ChallengeTitle).IsEqualTo("Capture target");
            await Assert.That(truncated.Items.Select(item => item.RuntimeInstanceId))
                .IsEquivalentTo([truncatedRuntimeId]);
            await Assert.That(complete.Items.Select(item => item.RuntimeInstanceId))
                .IsEquivalentTo([completeRuntimeId]);
        });
    }

    private static RuntimeTrafficCaptureQuery Query(
        Guid competitionId,
        bool? truncated) =>
        new(competitionId, null, null, null, truncated, 0, 20);

    private static RuntimeInstance Runtime(
        Guid runtimeId,
        Guid competitionId,
        Guid competitionChallengeId,
        DateTimeOffset now) =>
        new()
        {
            Id = runtimeId,
            CompetitionId = competitionId,
            CompetitionChallengeId = competitionChallengeId,
            Purpose = RuntimePurpose.Player,
            AccessMode = RuntimeAccessMode.DirectAndWsrx,
            RuntimeKind = RuntimeKind.Container,
            RuntimeProvider = RuntimeProvider.Docker,
            State = RuntimeState.Stopped,
            CreatedAt = now,
            RunningAt = now,
            StoppedAt = now.AddMinutes(1)
        };

    private static void AddSegment(
        NoCtfDbContext db,
        Guid competitionId,
        Guid competitionChallengeId,
        Guid runtimeId,
        DateTimeOffset occurredAt,
        long byteLength,
        bool truncated)
    {
        var fileId = Guid.CreateVersion7();
        db.Files.Add(new StoredFile
        {
            Id = fileId,
            ObjectKey = $"traffic-captures/{runtimeId:N}/{fileId:N}.pcapng",
            FileName = $"{fileId:N}.pcapng",
            ContentType = "application/vnd.tcpdump.pcap",
            ByteLength = byteLength,
            Sha256 = new byte[32],
            CreatedAt = occurredAt
        });
        db.CompetitionEvents.Add(new CompetitionEvent
        {
            Id = Guid.CreateVersion7(),
            CompetitionId = competitionId,
            Kind = CompetitionEventKind.RuntimeTrafficCaptureStored,
            Level = CompetitionEventLevel.Warning,
            Visibility = CompetitionEventVisibility.Staff,
            SubjectType = EntityReferenceKind.RuntimeInstance,
            SubjectId = runtimeId,
            RelatedType = EntityReferenceKind.File,
            RelatedId = fileId,
            PayloadJson = System.Text.Json.JsonSerializer.Serialize(new
            {
                schemaVersion = 1,
                competitionChallengeId,
                runtimeInstanceId = runtimeId,
                truncated
            }),
            OccurredAt = occurredAt
        });
    }
}
