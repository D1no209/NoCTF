using System.Formats.Tar;
using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using NoCTF.Application.GameplayFacts.Processing;
using NoCTF.Application.Messaging;
using NoCTF.Application.Runtime.Capacity;
using NoCTF.Application.Runtime.Instances;
using NoCTF.Application.Runtime.Provisioning;
using NoCTF.Domain.Gameplay;
using NoCTF.Domain.Runtime;
using NoCTF.Runner.Composition;
using NoCTF.Runner.Messages;

namespace NoCTF.Tests.Unit.Runner;

public sealed class AwdpCanonicalFixInputFlowTests
{
    [Test]
    [Arguments(TarEntryFormat.Pax)]
    [Arguments(TarEntryFormat.Gnu)]
    public async Task EnabledInput_ReusesCanonicalTarWithIndependentStreamsAndOneDownload(TarEntryFormat format)
    {
        var sourceArchive = CreateFixTarGzip(format);
        var http = new ArchiveHttpClientFactory(sourceArchive);
        var sandbox = new RecordingSandbox();
        var checker = new RecordingChecker();
        var outbox = new RecordingOutbox();
        var message = Message();
        var handler = CreateHandler(
            message,
            sourceArchive,
            http,
            sandbox,
            checker,
            outbox);

        await handler.Handle(message, CancellationToken.None);

        await Assert.That(http.RequestCount).IsEqualTo(1);
        await Assert.That(sandbox.CopyCalls).IsEqualTo(1);
        await Assert.That(checker.InputCalls).IsEqualTo(1);
        await Assert.That(checker.LegacyCalls).IsEqualTo(0);
        await Assert.That(sandbox.ArchiveStream).IsNotNull();
        await Assert.That(checker.ArchiveStream).IsNotNull();
        await Assert.That(ReferenceEquals(
            sandbox.ArchiveStream,
            checker.ArchiveStream)).IsFalse();
        await Assert.That(checker.ArchiveBytes).IsNotNull();
        await Assert.That(sandbox.ArchiveBytes).IsNotNull();
        await Assert.That(checker.ArchiveBytes!.SequenceEqual(sandbox.ArchiveBytes!)).IsTrue();
        await Assert.That(ReadEntryNames(checker.ArchiveBytes!))
            .Contains("noctf/fix/fix.sh");
        await Assert.That(outbox.Messages).IsEmpty();
    }

    [Test]
    public async Task TargetInputFailure_PublishesPlatformFailedAndSkipsChecker()
    {
        var sourceArchive = CreateFixTarGzip();
        var sandbox = new RecordingSandbox
        {
            CopyFailure = new IOException("provider-copy-failure")
        };
        var checker = new RecordingChecker();
        var outbox = new RecordingOutbox();
        var message = Message();
        var handler = CreateHandler(
            message,
            sourceArchive,
            new ArchiveHttpClientFactory(sourceArchive),
            sandbox,
            checker,
            outbox);

        await handler.Handle(message, CancellationToken.None);

        await Assert.That(checker.InputCalls).IsEqualTo(0);
        await Assert.That(outbox.Messages.OfType<AwdpFixResult>().Single().Outcome)
            .IsEqualTo(AwdpFixOutcome.PlatformFailed);
    }

    [Test]
    public async Task CheckerInputFailure_PublishesPlatformFailed()
    {
        var sourceArchive = CreateFixTarGzip();
        var checker = new RecordingChecker
        {
            InputFailure = new AwdpFixPlatformException(
                AwdpFixExecutionStage.CheckerInputPreparation,
                AwdpFixPlatformFailureCode.CheckerInputInjectionFailed,
                RuntimeProvider.Docker,
                new IOException("provider-input-failure"))
        };
        var outbox = new RecordingOutbox();
        var message = Message();
        var handler = CreateHandler(
            message,
            sourceArchive,
            new ArchiveHttpClientFactory(sourceArchive),
            new RecordingSandbox(),
            checker,
            outbox);

        await handler.Handle(message, CancellationToken.None);

        await Assert.That(outbox.Messages.OfType<AwdpFixResult>().Single().Outcome)
            .IsEqualTo(AwdpFixOutcome.PlatformFailed);
    }

    private static AwdpFixVerificationHandler CreateHandler(
        RunAwdpFixVerification message,
        byte[] sourceArchive,
        ArchiveHttpClientFactory http,
        RecordingSandbox sandbox,
        RecordingChecker checker,
        RecordingOutbox outbox)
    {
        var work = new AwdpFixWork(
            new AwdpFixArchive(
                new Uri("https://api.example/fix"),
                "download-token",
                "fix.tar.gz",
                sourceArchive.LongLength,
                SHA256.HashData(sourceArchive)),
            new ContainerReceipt(
                message.RuntimeInstanceId,
                RuntimeProvider.Docker,
                "target-container",
                RuntimeStatus.Running,
                new Dictionary<int, int>(),
                null,
                "target",
                NetworkId: "network-1",
                RuntimeInstanceId: message.RuntimeInstanceId),
            "fix.sh",
            ["/bin/sh", "/noctf/fix/fix.sh"],
            TimeSpan.FromSeconds(30),
            new AwdpCheckerWork(
                message.RuntimeInstanceId,
                RuntimeProvider.Docker,
                "checker:latest",
                ["/checker"],
                new Dictionary<string, string>(),
                "network-1",
                "target",
                30,
                new Uri("https://api.example/results"),
                "callback-token",
                TimeSpan.FromSeconds(30),
                FixInputEnabled: true));
        var reader = Substitute.For<IAwdpFixWorkReader>();
        reader.ClaimAsync(message, Arg.Any<CancellationToken>())
            .Returns(new AwdpFixWorkClaim(
                AwdpFixExecutionFenceDisposition.Execute,
                work));
        var lifetime = Substitute.For<IHostApplicationLifetime>();
        lifetime.ApplicationStopping.Returns(CancellationToken.None);
        return new(
            reader,
            new AwdpFixArchiveDownloader(http),
            new FixArchivePreparer(Options.Create(new FixVerificationOptions())),
            new RecordingProviderCatalog(sandbox),
            checker,
            [],
            Substitute.For<IRunnerCapacityGate>(),
            outbox,
            Options.Create(new RunnerOptions { Id = "runner-a", Pool = "pool-a" }),
            lifetime,
            TimeProvider.System,
            NullLogger<AwdpFixVerificationHandler>.Instance);
    }

    private static RunAwdpFixVerification Message() => new(
        Guid.CreateVersion7(),
        Guid.CreateVersion7(),
        Guid.CreateVersion7(),
        Guid.CreateVersion7(),
        DateTimeOffset.UtcNow.AddMinutes(5),
        "runner-a");

    private static byte[] CreateFixTarGzip(TarEntryFormat format = TarEntryFormat.Pax)
    {
        using var output = new MemoryStream();
        using (var gzip = new GZipStream(output, CompressionLevel.NoCompression, leaveOpen: true))
        using (var writer = new TarWriter(gzip, format, leaveOpen: false))
        {
            foreach (var (name, content) in new[] { ("fix.sh", "#!/bin/sh\nexit 0\n"), ("payload.txt", "same-canonical-input") })
            {
                TarEntry entry = format == TarEntryFormat.Gnu
                    ? new GnuTarEntry(TarEntryType.RegularFile, name)
                    : new PaxTarEntry(TarEntryType.RegularFile, name);
                entry.DataStream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(content));
                writer.WriteEntry(entry);
            }
        }
        return output.ToArray();
    }

    private static string[] ReadEntryNames(byte[] canonicalTar)
    {
        using var stream = new MemoryStream(canonicalTar);
        using var reader = new TarReader(stream);
        var names = new List<string>();
        while (reader.GetNextEntry() is { } entry)
            names.Add(entry.Name);
        return [.. names];
    }

    private sealed class ArchiveHttpClientFactory(byte[] archive) : IHttpClientFactory
    {
        public int RequestCount { get; private set; }

        public HttpClient CreateClient(string name) => new(new Handler(this, archive));

        private sealed class Handler(ArchiveHttpClientFactory owner, byte[] archive)
            : HttpMessageHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(
                HttpRequestMessage request,
                CancellationToken cancellationToken)
            {
                owner.RequestCount++;
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent(archive)
                });
            }
        }
    }

    private sealed class RecordingProviderCatalog(RecordingSandbox sandbox)
        : IRuntimeProviderCatalog
    {
        public IContainerSandboxLifecycle Sandbox(RuntimeProvider provider) => sandbox;
        public IContainerLifecycle Containers(RuntimeProvider provider) =>
            throw new NotSupportedException();
        public IComposeRuntime Compose(RuntimeProvider provider) =>
            throw new NotSupportedException();
        public IOvaRuntime Appliance(RuntimeProvider provider) =>
            throw new NotSupportedException();
    }

    private sealed class RecordingSandbox : IContainerSandboxLifecycle
    {
        public int CopyCalls { get; private set; }
        public Stream? ArchiveStream { get; private set; }
        public byte[]? ArchiveBytes { get; private set; }
        public Exception? CopyFailure { get; init; }

        public Task CopyArchiveAsync(
            ContainerReceipt receipt,
            Stream tarArchive,
            CancellationToken cancellationToken)
        {
            CopyCalls++;
            ArchiveStream = tarArchive;
            if (CopyFailure is not null)
                return Task.FromException(CopyFailure);
            return ReadAsync(tarArchive, cancellationToken);
        }

        private async Task ReadAsync(Stream archive, CancellationToken cancellationToken)
        {
            using var output = new MemoryStream();
            await archive.CopyToAsync(output, cancellationToken);
            ArchiveBytes = output.ToArray();
        }

        public Task<ContainerExecResult> ExecAsync(
            ContainerReceipt receipt,
            IReadOnlyList<string> command,
            TimeSpan timeout,
            CancellationToken cancellationToken) =>
            Task.FromResult(new ContainerExecResult(0, false));

        public Task<string> CreateIsolatedNetworkAsync(
            ContainerNetworkPolicyRequest request,
            CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task DeleteIsolatedNetworkAsync(
            string networkId,
            CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> IsolatedNetworkExistsAsync(
            string networkId,
            CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<ContainerExecResult> ExecWithInputAsync(
            ContainerReceipt receipt,
            IReadOnlyList<string> command,
            ReadOnlyMemory<byte> standardInput,
            TimeSpan timeout,
            CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class RecordingChecker : IAwdpCheckerExecutor
    {
        public int LegacyCalls { get; private set; }
        public int InputCalls { get; private set; }
        public Stream? ArchiveStream { get; private set; }
        public byte[]? ArchiveBytes { get; private set; }
        public Exception? InputFailure { get; init; }

        public Task<AwdpCheckerExecutionOutcome> ExecuteAsync(
            AwdpCheckerWork work,
            CancellationToken cancellationToken)
        {
            LegacyCalls++;
            return Task.FromResult(AwdpCheckerExecutionOutcome.Completed);
        }

        public async Task<AwdpCheckerExecutionOutcome> ExecuteAsync(
            AwdpCheckerWork work,
            OneShotInputArchive input,
            CancellationToken cancellationToken)
        {
            InputCalls++;
            ArchiveStream = input.Archive;
            if (InputFailure is not null)
                throw InputFailure;
            using var output = new MemoryStream();
            await input.Archive.CopyToAsync(output, cancellationToken);
            ArchiveBytes = output.ToArray();
            return AwdpCheckerExecutionOutcome.Completed;
        }
    }

    private sealed class RecordingOutbox : ITransactionalMessageOutbox
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
        public ValueTask ScheduleToRunnerNodeAsync<T>(T message, DateTimeOffset scheduledAt)
            where T : IRunnerNodeMessage => ValueTask.CompletedTask;
        public Task FlushOutgoingMessagesAsync() => Task.CompletedTask;
    }
}
