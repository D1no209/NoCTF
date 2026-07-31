using Microsoft.EntityFrameworkCore;
using NSubstitute;
using NoCTF.Application.Challenges.Flags;
using NoCTF.Application.Messaging;
using NoCTF.Application.Runtime.Instances;
using NoCTF.Application.Runtime.Provisioning;
using NoCTF.Domain.Challenges;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Identity;
using NoCTF.Domain.Runtime;
using NoCTF.Domain.Teams;
using NoCTF.Infrastructure.Persistence;
using NoCTF.Infrastructure.Runtime.Administration;
using NoCTF.Infrastructure.Runtime.Instances;
using NoCTF.Tests.Fixtures;
using Testcontainers.PostgreSql;

namespace NoCTF.Tests.Integration.Runtime;

[Category("Integration")]
public sealed class RuntimePublishedPortAllocatorTests
{
    [Test]
    [Timeout(300_000)]
    public async Task Allocation_is_concurrent_safe_historical_and_globally_exclusive(
        CancellationToken cancellationToken)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var postgres = new PostgreSqlBuilder("postgres:17-alpine")
                .WithDatabase("noctf_runtime_ports")
                .WithUsername("postgres")
                .WithPassword("postgres")
                .Build();
            await postgres.StartAsync(cancellationToken);
            var options = new DbContextOptionsBuilder<NoCtfDbContext>()
                .UseNpgsql(postgres.GetConnectionString())
                .UseSnakeCaseNamingConvention()
                .Options;
            var fixture = await SeedAsync(options, cancellationToken);
            var onePort = new RuntimePublishedPortRange(61000, 61000);

            var competing = await Task.WhenAll(
                AllocateAsync(options, fixture.FirstRuntimeId, onePort, cancellationToken),
                AllocateAsync(options, fixture.SecondRuntimeId, onePort, cancellationToken));
            var winner = competing.Single(result => result.Result.Failure is null);
            var loser = competing.Single(result =>
                result.Result.Failure == RuntimePublishedPortAllocationFailure.RangeExhausted);

            await Assert.That(winner.Result.Mappings.Single().HostPort).IsEqualTo(61000);
            await SetStoppedAsync(options, winner.RuntimeId, cancellationToken);
            var reusedByOtherCompetition = await AllocateAsync(
                options,
                loser.RuntimeId,
                onePort,
                cancellationToken);
            await Assert.That(reusedByOtherCompetition.Result.Failure).IsNull();
            await Assert.That(reusedByOtherCompetition.Result.Mappings.Single().HostPort)
                .IsEqualTo(61000);

            await SetStoppedAsync(options, loser.RuntimeId, cancellationToken);
            var replacementId = await AddReplacementAsync(
                options,
                winner.RuntimeId,
                cancellationToken);
            var historicalReuse = await AllocateAsync(
                options,
                replacementId,
                onePort,
                cancellationToken);
            await Assert.That(historicalReuse.Result.Failure)
                .IsEqualTo(RuntimePublishedPortAllocationFailure.RangeExhausted);

            var mismatch = await AllocateAsync(
                options,
                winner.RuntimeId,
                onePort,
                cancellationToken,
                []);
            await Assert.That(mismatch.Result.Failure)
                .IsEqualTo(RuntimePublishedPortAllocationFailure.TargetMismatch);

            var twoPorts = new RuntimePublishedPortRange(61000, 61001);
            var sameCompetition = await Task.WhenAll(
                AllocateAsync(
                    options,
                    fixture.ThirdRuntimeId,
                    twoPorts,
                    cancellationToken),
                AllocateAsync(
                    options,
                    fixture.FourthRuntimeId,
                    twoPorts,
                    cancellationToken));
            await Assert.That(sameCompetition.All(item => item.Result.Failure is null)).IsTrue();
            await Assert.That(sameCompetition
                    .Select(item => item.Result.Mappings.Single().HostPort)
                    .Distinct())
                .Count()
                .IsEqualTo(2);
            await SetStoppedAsync(options, fixture.ThirdRuntimeId, cancellationToken);
            await SetStoppedAsync(options, fixture.FourthRuntimeId, cancellationToken);

            var atomicExhaustion = await AllocateAsync(
                options,
                fixture.FifthRuntimeId,
                onePort,
                cancellationToken,
                [new(null, 8080), new(null, 8443)]);
            await Assert.That(atomicExhaustion.Result.Failure)
                .IsEqualTo(RuntimePublishedPortAllocationFailure.RangeExhausted);
            await Assert.That(await PublishedPortCountAsync(
                    options,
                    fixture.FifthRuntimeId,
                    cancellationToken))
                .IsEqualTo(0);

            var idempotent = await Task.WhenAll(
                AllocateAsync(
                    options,
                    fixture.FifthRuntimeId,
                    onePort,
                    cancellationToken),
                AllocateAsync(
                    options,
                    fixture.FifthRuntimeId,
                    onePort,
                    cancellationToken));
            await Assert.That(idempotent.All(item => item.Result.Failure is null)).IsTrue();
            await Assert.That(idempotent
                    .Select(item => item.Result.Mappings.Single().HostPort)
                    .Distinct())
                .Count()
                .IsEqualTo(1);

            await AssertAdminTraceAsync(
                options,
                winner.RuntimeId,
                61000,
                cancellationToken);
        });
    }

    private static async Task AssertAdminTraceAsync(
        DbContextOptions<NoCtfDbContext> options,
        Guid runtimeId,
        int hostPort,
        CancellationToken cancellationToken)
    {
        await using var db = new NoCtfDbContext(options);
        var owner = await db.RuntimeInstances.AsNoTracking()
            .Where(item => item.Id == runtimeId)
            .Select(item => new { item.CompetitionId, item.TeamId })
            .SingleAsync(cancellationToken);
        var store = new AdminRuntimeStore(
            db,
            Substitute.For<IChallengeRuntimeTemplateCatalog>(),
            Substitute.For<IRuntimePlacementPolicy>(),
            Substitute.For<IPerTeamRuntimeFlagStore>(),
            Substitute.For<ITransactionalMessageOutbox>());

        var items = await store.ListAsync(
            new(
                owner.CompetitionId,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                hostPort),
            null,
            null,
            10,
            cancellationToken);

        var item = items.Single();
        await Assert.That(item.Id).IsEqualTo(runtimeId);
        await Assert.That(item.TeamId).IsEqualTo(owner.TeamId);
        await Assert.That(item.PublishedPorts!.Single().HostPort).IsEqualTo(hostPort);
    }

    private static async Task<AllocationAttempt> AllocateAsync(
        DbContextOptions<NoCtfDbContext> options,
        Guid runtimeId,
        RuntimePublishedPortRange range,
        CancellationToken cancellationToken,
        IReadOnlyList<RuntimePublishedPortTarget>? targets = null)
    {
        await using var db = new NoCtfDbContext(options);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var runtime = await db.RuntimeInstances.SingleAsync(
            item => item.Id == runtimeId,
            cancellationToken);
        var allocator = new PostgresRuntimePublishedPortAllocator(db, range);
        var result = await allocator.AllocateAsync(
            runtime,
            targets ?? [new(null, 8080)],
            DateTimeOffset.UtcNow,
            cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new(runtimeId, result);
    }

    private static async Task SetStoppedAsync(
        DbContextOptions<NoCtfDbContext> options,
        Guid runtimeId,
        CancellationToken cancellationToken)
    {
        await using var db = new NoCtfDbContext(options);
        var runtime = await db.RuntimeInstances.SingleAsync(
            item => item.Id == runtimeId,
            cancellationToken);
        runtime.State = RuntimeState.Stopped;
        runtime.StoppedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
    }

    private static async Task<int> PublishedPortCountAsync(
        DbContextOptions<NoCtfDbContext> options,
        Guid runtimeId,
        CancellationToken cancellationToken)
    {
        await using var db = new NoCtfDbContext(options);
        return await db.RuntimeInstances.AsNoTracking()
            .Where(item => item.Id == runtimeId)
            .Select(item => item.PublishedPorts.Count)
            .SingleAsync(cancellationToken);
    }

    private static async Task<Guid> AddReplacementAsync(
        DbContextOptions<NoCtfDbContext> options,
        Guid predecessorId,
        CancellationToken cancellationToken)
    {
        await using var db = new NoCtfDbContext(options);
        var predecessor = await db.RuntimeInstances.AsNoTracking().SingleAsync(
            item => item.Id == predecessorId,
            cancellationToken);
        var replacement = Runtime(
            Guid.CreateVersion7(),
            predecessor.CompetitionId,
            predecessor.CompetitionChallengeId,
            predecessor.TeamId!.Value,
            2);
        replacement.ReplacesRuntimeInstanceId = predecessorId;
        db.RuntimeInstances.Add(replacement);
        await db.SaveChangesAsync(cancellationToken);
        return replacement.Id;
    }

    private static async Task<Fixture> SeedAsync(
        DbContextOptions<NoCtfDbContext> options,
        CancellationToken cancellationToken)
    {
        await using var db = new NoCtfDbContext(options);
        await db.Database.EnsureCreatedAsync(cancellationToken);
        var now = DateTimeOffset.UtcNow;
        var ownerId = Guid.CreateVersion7(now);
        db.Users.Add(User(ownerId, "port-owner", now));

        var competitionIds = Enumerable.Range(0, 4)
            .Select(index => Guid.CreateVersion7(now.AddTicks(index + 1)))
            .ToArray();
        foreach (var (competitionId, index) in competitionIds.Select((id, index) => (id, index)))
            db.Competitions.Add(Competition(competitionId, ownerId, index, now));

        var runtimeIds = new Guid[5];
        for (var index = 0; index < runtimeIds.Length; index++)
        {
            var competitionIndex = index switch
            {
                < 2 => index,
                < 4 => 2,
                _ => 3
            };
            var userId = Guid.CreateVersion7(now.AddTicks(10 + index * 5));
            var teamId = Guid.CreateVersion7(now.AddTicks(11 + index * 5));
            var challengeId = Guid.CreateVersion7(now.AddTicks(12 + index * 5));
            var competitionChallengeId = Guid.CreateVersion7(now.AddTicks(13 + index * 5));
            runtimeIds[index] = Guid.CreateVersion7(now.AddTicks(14 + index * 5));
            db.Users.Add(User(userId, $"port-player-{index}", now));
            db.Teams.Add(new Team
            {
                Id = teamId,
                CompetitionId = competitionIds[competitionIndex],
                Name = $"Port Team {index}",
                NormalizedName = $"PORT TEAM {index}",
                CaptainId = userId,
                MemberIds = [userId],
                InvitationToken = index.ToString().PadLeft(32, 'p'),
                RegistrationStatus = TeamRegistrationStatus.Approved,
                RegisteredAt = now
            });
            db.Challenges.Add(new Challenge
            {
                Id = challengeId,
                OwnerId = ownerId,
                Mode = GameMode.Ctf,
                Title = $"Port Challenge {index}",
                CreatedAt = now,
                UpdatedAt = now
            });
            db.CompetitionChallenges.Add(new CompetitionChallenge
            {
                Id = competitionChallengeId,
                CompetitionId = competitionIds[competitionIndex],
                ChallengeId = challengeId,
                BaseScore = 100,
                Order = index + 1,
                IsPublished = true,
                UpdatedAt = now
            });
            db.RuntimeInstances.Add(Runtime(
                runtimeIds[index],
                competitionIds[competitionIndex],
                competitionChallengeId,
                teamId,
                1));
        }
        await db.SaveChangesAsync(cancellationToken);
        return new(
            runtimeIds[0],
            runtimeIds[1],
            runtimeIds[2],
            runtimeIds[3],
            runtimeIds[4]);
    }

    private static Competition Competition(
        Guid id,
        Guid ownerId,
        int index,
        DateTimeOffset now) => new()
    {
        Id = id,
        OwnerId = ownerId,
        Title = $"Port Competition {index}",
        Mode = GameMode.Ctf,
        Status = CompetitionStatus.Running,
        MaxConcurrentRuntimeInstancesPerTeam = 1,
        StartAt = now.AddMinutes(-1),
        EndAt = now.AddHours(1),
        RunningSince = now,
        FlagDerivationSecret = Enumerable.Range(1, 32).Select(value => (byte)value).ToArray(),
        CreatedAt = now,
        UpdatedAt = now,
        ConfigurationUpdatedAt = now
    };

    private static RuntimeInstance Runtime(
        Guid id,
        Guid competitionId,
        Guid competitionChallengeId,
        Guid teamId,
        int generation) => new()
    {
        Id = id,
        CompetitionId = competitionId,
        CompetitionChallengeId = competitionChallengeId,
        TeamId = teamId,
        Purpose = RuntimePurpose.Player,
        Generation = generation,
        RuntimeKind = RuntimeKind.Container,
        RuntimeProvider = RuntimeProvider.Docker,
        RunnerPool = "port-tests",
        State = RuntimeState.Queued,
        CreatedAt = DateTimeOffset.UtcNow
    };

    private static User User(Guid id, string name, DateTimeOffset now) => new()
    {
        Id = id,
        UserName = name,
        NormalizedUserName = name.ToUpperInvariant(),
        Email = $"{name}@example.test",
        NormalizedEmail = $"{name.ToUpperInvariant()}@EXAMPLE.TEST",
        PasswordHash = "test",
        CreatedAt = now,
        UpdatedAt = now
    };

    private sealed record Fixture(
        Guid FirstRuntimeId,
        Guid SecondRuntimeId,
        Guid ThirdRuntimeId,
        Guid FourthRuntimeId,
        Guid FifthRuntimeId);

    private sealed record AllocationAttempt(
        Guid RuntimeId,
        RuntimePublishedPortAllocationResult Result);
}
