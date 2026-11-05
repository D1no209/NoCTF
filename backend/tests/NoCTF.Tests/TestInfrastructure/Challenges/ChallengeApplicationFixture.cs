using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NATS.Client.Core;
using NATS.Client.JetStream.Models;
using NATS.Net;
using NoCTF.Domain.Challenges;
using NoCTF.Domain.Challenges.Capabilities;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Identity;
using NoCTF.Application.Messaging;
using NoCTF.GameModes.Registration;
using NoCTF.Hosting;
using NoCTF.Infrastructure.Challenges.Configuration;
using NoCTF.Infrastructure.Challenges.Management;
using NoCTF.Infrastructure.Competitions.Events;
using NoCTF.Infrastructure.Messaging;
using NoCTF.Infrastructure.Persistence;
using Testcontainers.PostgreSql;
using Wolverine;

namespace NoCTF.Tests;

internal sealed class ChallengeApplicationFixture : IAsyncDisposable
{
    private readonly PostgreSqlContainer postgres = new PostgreSqlBuilder("postgres:17.10-alpine3.24@sha256:742f40ea20b9ff2ff31db5458d127452988a2164df9e17441e191f3b72252193")
        .WithDatabase($"noctf_challenges_{Guid.NewGuid():N}").Build();
    private readonly IContainer nats = new ContainerBuilder("docker.m.daocloud.io/library/nats:2.14.7-alpine@sha256:4063edae0717ba5f7501bfde75f97fd9b57f5b93597b92c70b6a6fbbf6a74e06")
        .WithPortBinding(4222, true).WithCommand("-js").WithWaitStrategy(Wait.ForUnixContainer().UntilInternalTcpPortIsAvailable(4222)).Build();
    private IHost? host;
    private NatsConnection? connection;
    public NoCtfDbContext Db { get; private set; } = null!;
    public ChallengeManagementStore Challenges { get; private set; } = null!;
    public ChallengeConfigurationStore Configurations { get; private set; } = null!;
    public IPostCommitMessagePublisher Publisher { get; private set; } = null!;
    public IMessageBus MessageBus => host!.Services.GetRequiredService<IMessageBus>();
    public string NatsUrl => $"nats://{nats.Hostname}:{nats.GetMappedPublicPort(4222)}";
    public IHostApplicationLifetime Lifetime => host!.Services.GetRequiredService<IHostApplicationLifetime>();
    public GameModeChallengeConfigurationCatalog Catalog { get; } = new();
    public Guid OwnerId { get; } = Guid.NewGuid();
    public Guid CompetitionId { get; } = Guid.NewGuid();
    public Guid TemplateId { get; } = Guid.NewGuid();
    public DateTimeOffset Now { get; } = DateTimeOffset.UtcNow;

    public static async Task<ChallengeApplicationFixture> CreateAsync(CompetitionStatus status, CancellationToken ct)
    {
        var fixture = new ChallengeApplicationFixture();
        try
        {
            await Task.WhenAll(fixture.postgres.StartAsync(ct), fixture.nats.StartAsync(ct));
            var url = $"nats://{fixture.nats.Hostname}:{fixture.nats.GetMappedPublicPort(4222)}";
            var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:Nats"] = url }).Build();
            fixture.host = Host.CreateDefaultBuilder().UseWolverine(options =>
            {
                options.Discovery.DisableConventionalDiscovery();
                options.ConfigureNoCtfPersistence(configuration, HostRoles.Only(HostRole.Api));
                options.ConfigureNoCtfMessageRouting(configuration, HostRoles.Only(HostRole.Api));
            }).Build();
            await fixture.host.StartAsync(ct);
            fixture.connection = new(new NatsOpts { Url = url });
            await fixture.connection.CreateJetStreamContext().CreateOrUpdateConsumerAsync(NatsSubjects.EventsStream,
                new ConsumerConfig("challenge-application-events") { AckPolicy = ConsumerConfigAckPolicy.Explicit, FilterSubject = "noctf.v2.events.>" }, ct);
            fixture.Db = new(new DbContextOptionsBuilder<NoCtfDbContext>().UseNpgsql(fixture.postgres.GetConnectionString()).Options);
            await fixture.Db.Database.EnsureCreatedAsync(ct);
            var services = fixture.host.Services;
            var publisher = new WolverinePostCommitMessagePublisher(services.GetRequiredService<IMessageBus>(), fixture.Db,
                services.GetRequiredService<ILogger<WolverinePostCommitMessagePublisher>>());
            fixture.Publisher = publisher;
            var events = new CompetitionEventStore(fixture.Db, publisher);
            fixture.Challenges = new(fixture.Db, publisher, new ChallengeRuntimeTemplateCatalog(), events);
            fixture.Configurations = new(fixture.Db, publisher, events);
            fixture.Db.Users.Add(new() { Id = fixture.OwnerId, UserName = "author", NormalizedUserName = "AUTHOR", Email = "author@example.test",
                NormalizedEmail = "AUTHOR@EXAMPLE.TEST", Role = UserRole.Organizer, AccountStatus = UserAccountStatus.Active });
            fixture.Db.Competitions.Add(new CtfCompetition { Id = fixture.CompetitionId, OwnerId = fixture.OwnerId, Title = "Competition", Status = status,
                StartAt = fixture.Now.AddHours(-1), EndAt = fixture.Now.AddHours(1), ModeConfiguration = CompetitionModeConfigurationDefaults.Create(GameMode.Ctf, fixture.CompetitionId) });
            fixture.Db.Challenges.Add(new() { Id = fixture.TemplateId, OwnerId = fixture.OwnerId, Title = "Reusable", Direction = "Web",
                Definition = new() { Capabilities = [new BreakCapabilityDefinition()] } });
            await fixture.Db.SaveChangesAsync(ct); return fixture;
        }
        catch { await fixture.DisposeAsync(); throw; }
    }

    public async Task AssertPublishedEventsAsync(CancellationToken ct)
    {
        await connection!.PingAsync(ct);
        var expected = await Db.CompetitionEvents.CountAsync(ct) * 3;
        var stream = await connection.CreateJetStreamContext().GetStreamAsync(NatsSubjects.EventsStream, cancellationToken: ct);
        for (var attempt = 0; stream.Info.State.Messages < expected && attempt < 50; attempt++)
        {
            await Task.Delay(100, ct);
            stream = await connection.CreateJetStreamContext().GetStreamAsync(NatsSubjects.EventsStream, cancellationToken: ct);
        }
        await Assert.That((long)stream.Info.State.Messages).IsEqualTo((long)expected);
        await Assert.That(await Db.CompetitionEvents.CountAsync(ct)).IsGreaterThan(0);
    }

    public async ValueTask DisposeAsync()
    {
        if (Db is not null) await Db.DisposeAsync();
        if (host is not null) { await host.StopAsync(); host.Dispose(); }
        if (connection is not null) await connection.DisposeAsync();
        await nats.DisposeAsync(); await postgres.DisposeAsync();
    }
}
