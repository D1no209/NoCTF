using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using NoCTF.Application.Challenges.Flags;
using NoCTF.Application.Messaging;
using NoCTF.Application.Runtime.Instances;
using NoCTF.Application.Runtime.Provisioning;
using NoCTF.Domain.Challenges;
using NoCTF.Domain.Identity;
using NoCTF.Domain.Runtime;
using NoCTF.GameModes.Registration;
using NoCTF.Infrastructure.Persistence;
using NoCTF.Infrastructure.Runtime.Administration;
using NSubstitute;
using Testcontainers.PostgreSql;

namespace NoCTF.Tests.Integration.Persistence;

[Category("Integration")]
public sealed class AdminRuntimeEmptyPagePersistenceTests
{
    [Test]
    [Timeout(300_000)]
    public async Task Nonempty_platform_runtime_page_does_not_reload_its_runtime_rows(
        CancellationToken cancellationToken)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var postgres = new PostgreSqlBuilder("postgres:17.10-alpine3.24@sha256:742f40ea20b9ff2ff31db5458d127452988a2164df9e17441e191f3b72252193")
                .WithDatabase("noctf_nonempty_platform_runtime_page")
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
            var challengeId = Guid.CreateVersion7();
            var runtimeId = Guid.CreateVersion7();
            await using (var setup = new NoCtfDbContext(options))
            {
                await setup.Database.EnsureCreatedAsync(cancellationToken);
                setup.Users.Add(new User
                {
                    Id = ownerId, UserName = "runtime-owner", NormalizedUserName = "RUNTIME-OWNER",
                    Email = "runtime-owner@example.test", PasswordHash = "test",
                    Kind = UserKind.Human, Role = UserRole.Administrator,
                    CreatedAt = now, UpdatedAt = now
                });
                setup.Challenges.Add(new CtfChallenge
                {
                    Id = challengeId, OwnerId = ownerId, Title = "Runtime template",
                    Direction = "Web", Definition = TestConfigurations.Definition(NoCTF.Domain.Competitions.GameMode.Ctf),
                    CreatedAt = now, UpdatedAt = now
                });
                var runtime = RuntimeInstanceGeneratedCatalog.Create(RuntimePurpose.TemplateTest);
                runtime.Id = runtimeId;
                runtime.ChallengeId = challengeId;
                runtime.RuntimeKind = RuntimeKind.Container;
                runtime.RuntimeProvider = RuntimeProvider.Docker;
                runtime.State = RuntimeState.Queued;
                runtime.CreatedAt = now;
                setup.RuntimeInstances.Add(runtime);
                await setup.SaveChangesAsync(cancellationToken);
            }

            var counter = new QueryCounter();
            var measuredOptions = new DbContextOptionsBuilder<NoCtfDbContext>()
                .UseNpgsql(postgres.GetConnectionString())
                .UseSnakeCaseNamingConvention()
                .AddInterceptors(counter)
                .Options;
            await using var db = new NoCtfDbContext(measuredOptions);
            var store = new AdminRuntimeStore(
                db,
                new ChallengeRuntimeTemplateCatalog(),
                Substitute.For<IRuntimePlacementPolicy>(),
                Substitute.For<IPerTeamRuntimeFlagStore>(),
                Substitute.For<IPostCommitMessagePublisher>());

            var page = await store.ListActiveContainersPageAsync(
                new PlatformRuntimeFilter(), 0, 10, true, cancellationToken);

            await Assert.That(page.Total).IsEqualTo(1);
            await Assert.That(page.Items.Single().Runtime.Id).IsEqualTo(runtimeId);
            await Assert.That(counter.ReaderCount).IsLessThanOrEqualTo(4);
        });
    }

    [Test]
    [Timeout(300_000)]
    public async Task Empty_platform_runtime_page_skips_attribution_and_title_queries(
        CancellationToken cancellationToken)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var postgres = new PostgreSqlBuilder("postgres:17.10-alpine3.24@sha256:742f40ea20b9ff2ff31db5458d127452988a2164df9e17441e191f3b72252193")
                .WithDatabase("noctf_empty_platform_runtime_page")
                .WithUsername("postgres")
                .WithPassword("postgres")
                .Build();
            await postgres.StartAsync(cancellationToken);
            var options = new DbContextOptionsBuilder<NoCtfDbContext>()
                .UseNpgsql(postgres.GetConnectionString())
                .UseSnakeCaseNamingConvention()
                .Options;
            await using (var setup = new NoCtfDbContext(options))
                await setup.Database.EnsureCreatedAsync(cancellationToken);

            var counter = new QueryCounter();
            var measuredOptions = new DbContextOptionsBuilder<NoCtfDbContext>()
                .UseNpgsql(postgres.GetConnectionString())
                .UseSnakeCaseNamingConvention()
                .AddInterceptors(counter)
                .Options;
            await using var db = new NoCtfDbContext(measuredOptions);
            var store = new AdminRuntimeStore(
                db,
                new ChallengeRuntimeTemplateCatalog(),
                Substitute.For<IRuntimePlacementPolicy>(),
                Substitute.For<IPerTeamRuntimeFlagStore>(),
                Substitute.For<IPostCommitMessagePublisher>());

            var page = await store.ListActiveContainersPageAsync(
                new PlatformRuntimeFilter(), 0, 10, true, cancellationToken);

            await Assert.That(page.Total).IsEqualTo(0);
            await Assert.That(page.Items).IsEmpty();
            await Assert.That(counter.ReaderCount).IsLessThanOrEqualTo(2);
        });
    }

    private sealed class QueryCounter : DbCommandInterceptor
    {
        public int ReaderCount { get; private set; }

        public override ValueTask<DbDataReader> ReaderExecutedAsync(
            DbCommand command,
            CommandExecutedEventData eventData,
            DbDataReader result,
            CancellationToken cancellationToken = default)
        {
            ReaderCount++;
            return ValueTask.FromResult(result);
        }
    }
}
