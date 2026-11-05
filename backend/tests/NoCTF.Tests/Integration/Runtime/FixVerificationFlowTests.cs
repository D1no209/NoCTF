using System.Formats.Tar;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using DotNet.Testcontainers.Builders;
using FluentStorage;
using FluentStorage.Storage;
using FastEndpoints;
using FastEndpoints.Swagger;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NoCTF.API.Composition;
using NoCTF.API.Endpoints.Internal;
using NoCTF.Application.Authentication;
using NoCTF.Application.Challenges.Capabilities;
using NoCTF.Application.GameplayFacts.PatchUploads;
using NoCTF.Application.GameplayFacts.Processing;
using NoCTF.Application.Messaging;
using NoCTF.Application.Runtime.Provisioning;
using NoCTF.Domain.Challenges;
using NoCTF.Domain.Challenges.Capabilities;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Gameplay;
using NoCTF.Domain.Runtime;
using NoCTF.Domain.Storage;
using NoCTF.Domain.Teams;
using NoCTF.GameModes.Registration;
using NoCTF.Hosting;
using NATS.Client.Core;
using NATS.Client.JetStream.Models;
using NATS.Net;
using NoCTF.Infrastructure.Authentication;
using NoCTF.Infrastructure.Challenges.Capabilities;
using NoCTF.Infrastructure.Challenges.Flags;
using NoCTF.Infrastructure.Competitions.Events;
using NoCTF.Infrastructure.GameplayFacts.PatchUploads;
using NoCTF.Infrastructure.GameplayFacts.Processing;
using NoCTF.Infrastructure.Persistence;
using NoCTF.Infrastructure.Runtime.Capacity;
using NoCTF.Runner.Challenges.Capabilities;
using NoCTF.Runner.Composition;
using NoCTF.Runner.Messages;

namespace NoCTF.Tests.Integration.Runtime;

[Category("Integration"), NotInParallel]
public sealed class FixVerificationFlowTests
{
    public enum FlowScenario { Completed, CallerCanceled, HostShutdown, ArchiveUnavailable, ReceivedBeforeScoringCutoff, ReceivedAtScoringCutoff, OpeningChangedAfterAdmission, ClosingChangedAfterAdmission }
    private const string PythonImage = "docker.m.daocloud.io/library/python@sha256:7984cf56ceaddebe4fefeb75d64000b9f45d8b77b11a09b24524da6cdc3af0c8";

    [Test, Arguments(TarEntryFormat.Pax, true, FlowScenario.Completed), Arguments(TarEntryFormat.Gnu, true, FlowScenario.Completed),
        Arguments(TarEntryFormat.Pax, false, FlowScenario.Completed), Arguments(TarEntryFormat.Pax, true, FlowScenario.CallerCanceled),
        Arguments(TarEntryFormat.Pax, true, FlowScenario.HostShutdown), Arguments(TarEntryFormat.Pax, true, FlowScenario.ArchiveUnavailable),
        Arguments(TarEntryFormat.Pax, true, FlowScenario.ReceivedBeforeScoringCutoff), Arguments(TarEntryFormat.Pax, true, FlowScenario.ReceivedAtScoringCutoff),
        Arguments(TarEntryFormat.Pax, true, FlowScenario.OpeningChangedAfterAdmission), Arguments(TarEntryFormat.Pax, true, FlowScenario.ClosingChangedAfterAdmission), Timeout(300_000)]
    public async Task Actual_handler_downloads_once_applies_and_records_real_checker_evidence(TarEntryFormat format, bool repairs, FlowScenario scenario, CancellationToken ct)
    {
        await using var fixture = await RuntimeExecutionFixture.CreateAsync(ct);
        var directory = Path.Combine(AppContext.BaseDirectory, "fix-flow-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var script = Encoding.UTF8.GetBytes(repairs ? "printf repaired > /repair-state\n" : "printf vulnerable > /repair-state\n");
            var bytes = Archive(format, script);
            await File.WriteAllBytesAsync(Path.Combine(directory, "patch"), bytes, ct);
            var checkerScript = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Capabilities", "check_repair.py"), ct);
            var db = fixture.Db; var now = DateTimeOffset.UtcNow; var deadline = now.AddMinutes(3);
            var competitionId = Guid.NewGuid(); var challengeId = Guid.NewGuid(); var teamId = Guid.NewGuid();
            var runtimeId = Guid.NewGuid(); var factId = Guid.NewGuid(); var uploadId = Guid.NewGuid();
            var definition = new ChallengeDefinition
            {
                Runtime = new ContainerChallengeRuntimeTemplate { Services = [new() { Name = "service-0", Image = RuntimeExecutionFixture.Image }] },
                Capabilities = [new BreakCapabilityDefinition { EnvironmentTargets = [new() { ServiceName = "service-0", VariableName = "CHALLENGE_FLAG" }] },
                    new FixCapabilityDefinition { TargetServiceName = "service-0", Entrypoint = "fix.sh" },
                    new CheckCapabilityDefinition { Image = PythonImage, TargetServiceName = "service-0", AcceptsFixInput = true,
                        Command = [new() { Position = 0, Value = "python" }, new() { Position = 1, Value = "-c" }, new() { Position = 2, Value = checkerScript }],
                        Environment = [new() { Name = "PATCH_SCRIPT_SHA256", Value = Convert.ToHexStringLower(SHA256.HashData(script)) }],
                        Requirements = [new() { Kind = CheckItemKind.Functionality }, new() { Kind = CheckItemKind.Vulnerability }] }]
            };
            var template = new Challenge { Id = Guid.NewGuid(), OwnerId = fixture.Application.OwnerId, Title = "Shared repair capabilities", Definition = definition };
            db.Challenges.Add(template);
            db.Competitions.Add(new AwdpCompetition { Id = competitionId, OwnerId = fixture.Application.OwnerId, Title = "Real verification",
                Status = CompetitionStatus.Running, StartAt = now.AddMinutes(-1), EndAt = deadline.AddHours(1),
                FlagDerivationSecret = RandomNumberGenerator.GetBytes(32),
                ModeConfiguration = CompetitionModeConfigurationDefaults.Create(GameMode.Awdp, competitionId) });
            db.CompetitionChallenges.Add(new AwdpCompetitionChallenge { Id = challengeId, CompetitionId = competitionId, ChallengeId = template.Id,
                IsPublished = true, Rules = new AwdpCompetitionChallengeRules { RequireBreakBeforeFix = false } });
            db.Teams.Add(new Team { Id = teamId, CompetitionId = competitionId, CaptainId = fixture.Application.OwnerId,
                MemberIds = [fixture.Application.OwnerId], Name = "repair-team", InvitationToken = new string('r', 32), RegistrationStatus = TeamRegistrationStatus.Approved });
            await db.SaveChangesAsync(ct);
            var message = await fixture.QueueAsync(1, ct, new FixTargetRuntimeInstance { Id = runtimeId, CompetitionId = competitionId,
                CompetitionChallengeId = challengeId, TeamId = teamId, RuntimeKind = RuntimeKind.Container, RuntimeProvider = RuntimeProvider.Docker,
                State = RuntimeState.Queued, CreatedAt = now });
            message = message with { Definition = message.Definition with { Services = [message.Definition.Services[0] with
                { Command = ["/bin/sh", "-c", "printf vulnerable > /repair-state; exec nc -ll -p 8080 -e /bin/cat /repair-state"] }] } };
            var provisioned = await fixture.Handler.ProvisionContainerAsync(message, ct) as NoCTF.Application.Runtime.Instances.RuntimeProvisioned
                ?? throw new InvalidOperationException("The repair target did not start.");
            await fixture.PersistAsync(provisioned, ct);
            var attackMessage = await fixture.QueueAsync(1, ct, new AttackTargetRuntimeInstance { Id = Guid.NewGuid(), CompetitionId = competitionId,
                CompetitionChallengeId = challengeId, TeamId = teamId, RuntimeKind = RuntimeKind.Container, RuntimeProvider = RuntimeProvider.Docker,
                State = RuntimeState.Queued, CreatedAt = now });
            var attackFlag = await new PerTeamRuntimeFlagStore(db).EnsureRuntimeInstanceAsync(competitionId, challengeId, teamId, attackMessage.RuntimeInstanceId, now, ct);
            await db.SaveChangesAsync(ct);
            attackMessage = attackMessage with { Definition = attackMessage.Definition with { Services = [attackMessage.Definition.Services[0] with
                { Environment = new Dictionary<string, string> { ["CHALLENGE_FLAG"] = attackFlag } }] } };
            var attackReply = await fixture.Handler.ProvisionContainerAsync(attackMessage, ct);
            if (attackReply is not NoCTF.Application.Runtime.Instances.RuntimeProvisioned attack)
                throw new InvalidOperationException($"The independent attack target did not start: {(attackReply is NoCTF.Application.Runtime.Instances.RuntimeProvisionFailed failure ? failure.FailureCode.ToString() : attackReply?.GetType().Name ?? "NoResponse")}.");
            await fixture.PersistAsync(attack, ct);
            var file = new StoredFile { Id = Guid.NewGuid(), ObjectKey = "patch", FileName = "fix.tar.gz", ContentType = "application/gzip",
                ByteLength = bytes.LongLength, Sha256 = SHA256.HashData(bytes), CreatedAt = now };
            db.Files.Add(file); db.PatchUploads.Add(new PatchUpload { Id = uploadId, CompetitionId = competitionId, CompetitionChallengeId = challengeId,
                RuntimeInstanceId = runtimeId, TeamId = teamId, UploadedByUserId = fixture.Application.OwnerId, FileId = file.Id, File = file, UploadedAt = now });
            db.GameplayFacts.Add(new FixAttemptGameplayFact { Id = factId, CompetitionId = competitionId, CompetitionChallengeId = challengeId,
                TeamId = teamId, ActorUserId = fixture.Application.OwnerId, ReferenceKind = GameplayFactReferenceKind.PatchUpload, ReferenceId = uploadId,
                State = GameplayFactState.Processing, OccurredAt = now, UpdatedAt = now,
                FixExecution = new() { GameplayFactId = factId, RuntimeInstanceId = runtimeId, TargetKind = FixTargetKind.Isolated,
                    Phase = FixExecutionPhase.Queued, Deadline = deadline } });
            (await db.RuntimeInstances.SingleAsync(value => value.Id == runtimeId, ct)).GameplayFactId = factId;
            await db.SaveChangesAsync(ct);

            var key = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
            var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
                { ["Authentication:SigningKey"] = key, ["RunnerScoring:SigningKey"] = key }).Build();
            var builder = WebApplication.CreateBuilder(); builder.WebHost.UseKestrel().UseUrls("http://0.0.0.0:0");
            builder.Services.AddFastEndpoints(options =>
            {
                options.DisableAutoDiscovery = true; options.Assemblies = [typeof(DownloadFixArchiveEndpoint).Assembly];
                options.Filter = type => type == typeof(DownloadFixArchiveEndpoint) || type == typeof(RecordCheckResultEndpoint) || type == typeof(RecordCheckResultValidator);
            });
            builder.Services.SwaggerDocument(); builder.Services.AddNoCtfAuthentication(configuration);
            builder.Services.AddDbContext<NoCtfDbContext>(options => options.UseNpgsql(db.Database.GetConnectionString()));
            builder.Services.AddDbContextFactory<NoCtfDbContext>(options => options.UseNpgsql(db.Database.GetConnectionString()), ServiceLifetime.Scoped);
            builder.Services.AddScoped<NoCTF.Infrastructure.Challenges.Testing.TemplateCapabilityTestExecutionStore>();
            builder.Services.AddScoped<NoCTF.Application.Challenges.Testing.ITemplateCapabilityCheckResultStore>(services => services.GetRequiredService<NoCTF.Infrastructure.Challenges.Testing.TemplateCapabilityTestExecutionStore>());
            builder.Services.AddSingleton(TimeProvider.System);
            builder.Services.AddSingleton(fixture.Application.MessageBus);
            builder.Services.AddScoped<IPostCommitMessagePublisher, NoCTF.Infrastructure.Messaging.WolverinePostCommitMessagePublisher>();
            builder.Services.AddScoped<NoCTF.Application.Competitions.Events.ICompetitionEventRecorder, CompetitionEventStore>();
            builder.Services.AddScoped<IInternalResultStore, InternalResultStore>(); builder.Services.AddScoped<RecordInternalResult>();
            builder.Services.AddScoped<IFixArchiveReader, FixArchiveReader>(); builder.Services.AddSingleton<IStore>(StorageFactory.Disk(directory));
            builder.Services.AddSingleton<IOptions<EmailVerificationProtectionOptions>>(Options.Create(new EmailVerificationProtectionOptions { EncryptionKey = key }));
            builder.Services.AddSingleton<PlatformSecretProtector>();
            builder.Services.AddSingleton<NoCTF.Application.Authentication.Mfa.IMfaCryptography, NoCTF.Infrastructure.Authentication.Mfa.MfaCryptography>();
            builder.Services.AddScoped<NoCTF.Application.Authentication.Mfa.IMfaAuthenticationStore, NoCTF.Infrastructure.Authentication.Mfa.MfaAuthenticationStore>();
            await using var server = builder.Build();
            var downloads = 0;
            server.Use(async (context, next) => { if (context.Request.Path.StartsWithSegments("/api/internal/v1/fixes")) Interlocked.Increment(ref downloads); await next(context); });
            server.UseAuthentication(); server.UseAuthorization(); server.UseNoCtfEndpoints(); await server.StartAsync(ct);
            var port = new Uri(server.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single()).Port;
            var baseUri = new Uri($"http://{Environment.MachineName}:{port}");
            await using var gateway = new ContainerBuilder(PythonImage).WithLabel("noctf.io/internal-role", "scoring-callback-gateway")
                .WithExtraHost("host.docker.internal", "host-gateway").WithEnvironment("API_PORT", port.ToString(System.Globalization.CultureInfo.InvariantCulture))
                .WithResourceMapping(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Capabilities", "callback_proxy.py"), "/work/")
                .WithCommand("python", "/work/callback_proxy.py").WithWaitStrategy(Wait.ForUnixContainer().UntilInternalTcpPortIsAvailable(port)).Build();
            await gateway.StartAsync(ct);
            var scoring = Options.Create(new RunnerScoringOptions { SigningKey = key, CallbackBaseUrl = baseUri });
            var reader = new FixExecutionWorkReader(fixture.Contexts, new FixExecutionFence(db, TimeProvider.System),
                new RunnerScoringTokenIssuer(scoring), scoring, TimeProvider.System);
            var progress = new FixExecutionProgressStore(fixture.Contexts);
            using var mutations = new RunnerResourceMutationCoordinator();
            using var clients = new ServiceCollection().AddHttpClient(FixArchiveDownloader.ClientName)
                .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { UseProxy = false }).Services.BuildServiceProvider();
            if (scenario == FlowScenario.Completed && format == TarEntryFormat.Pax && repairs)
            {
                using var invalidCallback = clients.GetRequiredService<IHttpClientFactory>().CreateClient(FixArchiveDownloader.ClientName);
                invalidCallback.DefaultRequestHeaders.Authorization = new("Bearer", new RunnerScoringTokenIssuer(scoring).IssueFixCheckResult(
                    new(RuntimeExecutionFixture.RunnerId, factId, runtimeId, deadline, DateTimeOffset.UtcNow)));
                using var missingResult = await invalidCallback.PostAsJsonAsync(new Uri(baseUri, "/api/internal/v1/capabilities/check-results"),
                    new { probes = new[] { new { kind = "Functionality" } }, observedAt = DateTimeOffset.UtcNow }, ct);
                await Assert.That(missingResult.StatusCode).IsEqualTo(System.Net.HttpStatusCode.BadRequest);
            }
            var handler = new FixExecutionHandler(reader, new(clients.GetRequiredService<IHttpClientFactory>()),
                new(Options.Create(new FixVerificationOptions())), Options.Create(new FixVerificationOptions()), fixture.Providers,
                new(new RuntimeFixExecutor(fixture.Providers, mutations, fixture.Capacity, fixture.RunnerOptions, progress, TimeProvider.System), TimeProvider.System),
                progress, new(new RuntimeCheckExecutor(fixture.Providers, new RuntimeExecutionOptions())), fixture.Reconcilers, fixture.Capacity,
                fixture.Application.Publisher, fixture.RunnerOptions, fixture.Application.Lifetime, TimeProvider.System,
                NullLogger<FixExecutionHandler>.Instance);
            await fixture.RefreshOwnerAsync(ct);
            var command = new RunFixExecution(factId, challengeId, uploadId, runtimeId, deadline, RuntimeExecutionFixture.RunnerId);
            if (scenario is FlowScenario.ReceivedBeforeScoringCutoff or FlowScenario.ReceivedAtScoringCutoff or FlowScenario.OpeningChangedAfterAdmission or FlowScenario.ClosingChangedAfterAdmission)
            {
                var changed = await db.CompetitionChallenges.SingleAsync(value => value.Id == challengeId, ct);
                if (scenario == FlowScenario.ReceivedBeforeScoringCutoff) changed.ScoringEndsAt = now.AddSeconds(1);
                if (scenario == FlowScenario.ReceivedAtScoringCutoff) changed.ScoringEndsAt = now;
                if (scenario == FlowScenario.OpeningChangedAfterAdmission) changed.AutoOpenAt = now.AddSeconds(1);
                if (scenario == FlowScenario.ClosingChangedAfterAdmission) changed.SubmissionDeadlineAt = now;
                changed.TimingRevision = Guid.NewGuid(); await db.SaveChangesAsync(ct);
            }
            if (scenario is FlowScenario.CallerCanceled or FlowScenario.HostShutdown or FlowScenario.ArchiveUnavailable)
            {
                await using var connection = new NatsConnection(new NatsOpts { Url = fixture.Application.NatsUrl });
                var consumer = await connection.CreateJetStreamContext().CreateOrUpdateConsumerAsync(NatsSubjects.GameplayStream,
                    new ConsumerConfig("fix-compensation") { FilterSubject = NatsSubjects.Subject(WorkerQueue.Gameplay), AckPolicy = ConsumerConfigAckPolicy.Explicit }, ct);
                if (scenario == FlowScenario.ArchiveUnavailable) await server.StopAsync(ct);
                using var canceled = new CancellationTokenSource(); canceled.Cancel();
                if (scenario == FlowScenario.HostShutdown)
                {
                    fixture.Application.Lifetime.StopApplication();
                    await Assert.That(() => handler.Handle(command, canceled.Token)).Throws<OperationCanceledException>();
                    await Assert.That(await consumer.NextAsync<byte[]>(opts: new() { Expires = TimeSpan.FromSeconds(1) }, cancellationToken: ct)).IsNull();
                    return;
                }
                await handler.Handle(command, scenario == FlowScenario.CallerCanceled ? canceled.Token : ct);
                var delivered = await consumer.NextAsync<byte[]>(opts: new() { Expires = TimeSpan.FromSeconds(10) }, cancellationToken: ct);
                await Assert.That(delivered).IsNotNull();
                var serializer = new JsonSerializerOptions(JsonSerializerDefaults.Web); serializer.Converters.Add(new JsonStringEnumConverter());
                var failure = JsonSerializer.Deserialize<CompleteFixExecutionWork>(delivered!.Data!, serializer)!;
                await delivered.AckAsync(cancellationToken: ct);
                await Assert.That(failure.GameplayFactId).IsEqualTo(factId); await Assert.That(failure.Result.Kind).IsEqualTo(FixWorkCompletionKind.PlatformFailed);
                db.ChangeTracker.Clear();
                var recorded = await new InternalResultStore(db, fixture.Application.Publisher, new CompetitionEventStore(db, fixture.Application.Publisher)).CompleteFixWorkAsync(failure, ct);
                await Assert.That(recorded).IsEqualTo(InternalResultDisposition.Applied);
                db.ChangeTracker.Clear();
                await Assert.That((await db.GameplayFacts.AsSplitQuery().SingleAsync(value => value.Id == factId, ct)).State).IsEqualTo(GameplayFactState.PlatformFailed);
                await AssertCleanupAsync(fixture, provisioned, attack, ct);
                return;
            }
            await using var completionConnection = new NatsConnection(new NatsOpts { Url = fixture.Application.NatsUrl });
            var completionConsumer = await completionConnection.CreateJetStreamContext().CreateOrUpdateConsumerAsync(NatsSubjects.GameplayStream,
                new ConsumerConfig("fix-work-completion") { FilterSubject = NatsSubjects.Subject(WorkerQueue.Gameplay), AckPolicy = ConsumerConfigAckPolicy.Explicit }, ct);
            await handler.Handle(command, ct);
            db.ChangeTracker.Clear();
            var pending = await db.GameplayFacts.AsSplitQuery().SingleAsync(value => value.Id == factId, ct);
            await Assert.That(pending.State).IsEqualTo(GameplayFactState.Processing);
            await Assert.That(pending.FixExecution!.Phase).IsEqualTo(FixExecutionPhase.Finalizing);
            await Assert.That(await db.CompetitionEvents.CountAsync(value => value.GameplayFactId == factId
                && value.Kind == NoCTF.Domain.Competitions.Events.CompetitionEventKind.ScoringRecorded, ct)).IsEqualTo(0);
            var deliveredCompletion = await completionConsumer.NextAsync<byte[]>(opts: new() { Expires = TimeSpan.FromSeconds(10) }, cancellationToken: ct)
                ?? throw new InvalidOperationException("The real Runner did not publish its work completion.");
            var completionJson = new JsonSerializerOptions(JsonSerializerDefaults.Web); completionJson.Converters.Add(new JsonStringEnumConverter());
            var completion = JsonSerializer.Deserialize<CompleteFixExecutionWork>(deliveredCompletion.Data!, completionJson)!;
            await deliveredCompletion.AckAsync(cancellationToken: ct);
            var results = new InternalResultStore(db, fixture.Application.Publisher, new CompetitionEventStore(db, fixture.Application.Publisher));
            await Assert.That(await results.CompleteFixWorkAsync(completion, ct)).IsEqualTo(InternalResultDisposition.Applied);
            db.ChangeTracker.Clear();
            await Assert.That((await db.GameplayFacts.SingleAsync(value => value.Id == factId, ct)).State).IsEqualTo(GameplayFactState.Processing);
            await AssertCleanupAsync(fixture, provisioned, attack, ct);
            db.ChangeTracker.Clear();
            await Assert.That(await results.FinalizeFixExecutionAsync(factId, runtimeId, DateTimeOffset.UtcNow, ct)).IsEqualTo(InternalResultDisposition.Applied);
            db.ChangeTracker.Clear();
            var completed = await db.GameplayFacts.AsSplitQuery().SingleAsync(value => value.Id == factId, ct);
            await Assert.That(completed.State).IsEqualTo(GameplayFactState.Completed);
            var expectedResult = !repairs ? GameplayFactResult.Wrong
                : scenario is FlowScenario.ReceivedAtScoringCutoff or FlowScenario.OpeningChangedAfterAdmission or FlowScenario.ClosingChangedAfterAdmission
                    ? GameplayFactResult.RightButDue : GameplayFactResult.Correct;
            await Assert.That(completed.Result).IsEqualTo(expectedResult);
            await Assert.That(completed.TimeEligibility).IsEqualTo(scenario == FlowScenario.OpeningChangedAfterAdmission ? GameplayFactTimeEligibility.NotOpened
                : scenario == FlowScenario.ClosingChangedAfterAdmission ? GameplayFactTimeEligibility.SubmissionClosed : GameplayFactTimeEligibility.Valid);
            await Assert.That(completed.CapabilityOutcomes.OfType<FixOutcome>().Single().Result).IsEqualTo(FixResult.Applied);
            await Assert.That(completed.CapabilityOutcomes.OfType<CheckOutcome>().Single().Probes.Count).IsEqualTo(2);
            await Assert.That(downloads).IsEqualTo(1);
            var eventCount = await db.CompetitionEvents.CountAsync(value => value.GameplayFactId == factId, ct);
            await handler.Handle(command, ct);
            await Assert.That(downloads).IsEqualTo(1);
            await Assert.That(await db.GameplayFacts.CountAsync(value => value.Id == factId, ct)).IsEqualTo(1);
            await Assert.That(await db.CompetitionEvents.CountAsync(value => value.GameplayFactId == factId, ct)).IsEqualTo(eventCount);
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true); }
    }

    private static async Task AssertCleanupAsync(RuntimeExecutionFixture fixture, NoCTF.Application.Runtime.Instances.RuntimeProvisioned target,
        NoCTF.Application.Runtime.Instances.RuntimeProvisioned attack, CancellationToken ct)
    {
        var stopped = await fixture.Handler.StopContainerAsync(new(target.RuntimeInstanceId, RuntimeExecutionFixture.RunnerId, DateTimeOffset.UtcNow), ct);
        await Assert.That(stopped).IsTypeOf<NoCTF.Application.Runtime.Instances.RuntimeStopped>();
        await new RuntimeStopWriteBackMessageHandler(fixture.Db, fixture.Application.Publisher,
            new CompetitionEventStore(fixture.Db, fixture.Application.Publisher), TimeProvider.System)
            .Handle((NoCTF.Application.Runtime.Instances.RuntimeStopped)stopped, ct);
        var receipt = (ContainerRuntimeReceiptData)target.ProviderReceipt;
        await Assert.That(await fixture.Containers.GetAsync(RuntimeProvider.Docker, receipt.Services.Single().ResourceId, ct)).IsNull();
        var attackStatus = await fixture.Providers.Runtime(RuntimeProvider.Docker).GetStatusAsync(((ContainerRuntimeReceiptData)attack.ProviderReceipt).ToReceipt(), ct);
        await Assert.That(attackStatus!.Status).IsEqualTo(RuntimeStatus.Running);
    }

    private static byte[] Archive(TarEntryFormat format, byte[] script)
    {
        using var output = new MemoryStream();
        using (var gzip = new GZipStream(output, CompressionLevel.Fastest, leaveOpen: true))
        using (var tar = new TarWriter(gzip, format, leaveOpen: true))
        using (var input = new MemoryStream(script))
            tar.WriteEntry(format == TarEntryFormat.Pax ? new PaxTarEntry(TarEntryType.RegularFile, "fix.sh") { DataStream = input }
                : new GnuTarEntry(TarEntryType.RegularFile, "fix.sh") { DataStream = input });
        return output.ToArray();
    }
}
