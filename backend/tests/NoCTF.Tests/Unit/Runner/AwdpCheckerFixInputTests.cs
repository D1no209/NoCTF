using System.Text;
using NoCTF.Application.Runtime.Provisioning;
using NoCTF.Application.Runtime.Instances;
using NoCTF.Domain.Runtime;
using NoCTF.Runner.Composition;
using NoCTF.Runner.Messages;

namespace NoCTF.Tests.Unit.Runner;

public sealed class AwdpCheckerFixInputTests
{
    [Test]
    public async Task ExecuteAsync_WithoutInput_UsesCurrentContractAndReadonlyRoot()
    {
        var runner = new RecordingOneShotRunner();
        var executor = new AwdpCheckerExecutor(new RecordingCatalog(runner));

        var outcome = await executor.ExecuteAsync(Work(), null, CancellationToken.None);

        await Assert.That(outcome).IsEqualTo(AwdpCheckerExecutionOutcome.Completed);
        await Assert.That(runner.InputCalls).IsEqualTo(1);
        await Assert.That(runner.Input).IsNull();
    }

    [Test]
    public async Task ExecuteAsync_WithInput_PassesSameReadableStreamAndWritableCheckerRoot()
    {
        var runner = new RecordingOneShotRunner();
        var executor = new AwdpCheckerExecutor(new RecordingCatalog(runner));
        await using var stream = new MemoryStream(Encoding.UTF8.GetBytes("prefix-canonical-tar"));
        stream.Position = "prefix-".Length;
        var input = new OneShotInputArchive(stream, OneShotInputArchive.RootDestinationPath);

        var outcome = await executor.ExecuteAsync(Work(), input, CancellationToken.None);

        await Assert.That(outcome).IsEqualTo(AwdpCheckerExecutionOutcome.Completed);
        await Assert.That(runner.InputCalls).IsEqualTo(1);
        await Assert.That(runner.Input).IsSameReferenceAs(input);
        await Assert.That(runner.Input!.Archive).IsSameReferenceAs(stream);
        await Assert.That(runner.Input.Archive.Position).IsEqualTo("prefix-".Length);
    }

    [Test]
    public async Task ExecuteAsync_InputPreparationFailure_MapsToStablePlatformFailure()
    {
        var runner = new RecordingOneShotRunner
        {
            InputFailure = new OneShotInputPreparationException("provider details")
        };
        var executor = new AwdpCheckerExecutor(new RecordingCatalog(runner));
        await using var stream = new MemoryStream([1, 2, 3]);
        var input = new OneShotInputArchive(stream, OneShotInputArchive.RootDestinationPath);

        Func<Task> action = async () =>
            _ = await executor.ExecuteAsync(Work(), input, CancellationToken.None);

        var exception = await Assert.That(action).Throws<AwdpFixPlatformException>();
        await Assert.That(exception!.FailureCode)
            .IsEqualTo(AwdpFixPlatformFailureCode.CheckerInputInjectionFailed);
        await Assert.That(exception.Stage)
            .IsEqualTo(AwdpFixExecutionStage.CheckerInputPreparation);
    }

    [Test]
    public async Task ExecuteAsync_InputPreparationTimeout_MapsToPlatformFailureNotCheckerVerdict()
    {
        var runner = new RecordingOneShotRunner { WaitForInputCancellation = true };
        var executor = new AwdpCheckerExecutor(new RecordingCatalog(runner));
        await using var stream = new MemoryStream([1, 2, 3]);
        var input = new OneShotInputArchive(stream, OneShotInputArchive.RootDestinationPath);
        var work = Work() with { Timeout = TimeSpan.FromMilliseconds(50) };

        Func<Task> action = async () =>
            _ = await executor.ExecuteAsync(work, input, CancellationToken.None);

        var exception = await Assert.That(action).Throws<AwdpFixPlatformException>();
        await Assert.That(exception!.FailureCode)
            .IsEqualTo(AwdpFixPlatformFailureCode.CheckerInputInjectionFailed);
    }

    [Test]
    public async Task ProtectedWorkAndInput_ToString_RedactsTokensAndArchiveDetails()
    {
        const string token = "secret-callback-token";
        const string archiveToken = "secret-download-token";
        var work = Work() with { CallbackToken = token };
        await using var stream = new MemoryStream(Encoding.UTF8.GetBytes("flag{secret-fix}"));
        var input = new OneShotInputArchive(stream, OneShotInputArchive.RootDestinationPath);
        var archive = new AwdpFixArchive(
            new Uri("https://api.example/fix?token=secret"),
            archiveToken,
            "sensitive-file-name.tar.gz",
            stream.Length,
            [1, 2, 3]);

        var text = string.Join('|', work, input, archive,
            new AwdpFixPlatformException(
                AwdpFixExecutionStage.CheckerInputPreparation,
                AwdpFixPlatformFailureCode.CheckerInputInjectionFailed,
                RuntimeProvider.Docker,
                new IOException("/private/runner/fix.tar")));

        await Assert.That(text).DoesNotContain(token);
        await Assert.That(text).DoesNotContain(archiveToken);
        await Assert.That(text).DoesNotContain("flag{secret-fix}");
        await Assert.That(text).DoesNotContain("sensitive-file-name.tar.gz");
        await Assert.That(text).DoesNotContain("/private/runner/fix.tar");
    }

    [Test]
    public async Task RunnerMessages_DoNotCarryStreamsOrOneShotInputArchives()
    {
        var forbidden = typeof(IRunnerNodeMessage).Assembly.GetTypes()
            .Where(type => typeof(IRunnerNodeMessage).IsAssignableFrom(type) && !type.IsInterface)
            .SelectMany(type => type.GetProperties()
                .Where(property => typeof(Stream).IsAssignableFrom(property.PropertyType)
                    || property.PropertyType == typeof(OneShotInputArchive))
                .Select(property => $"{type.FullName}.{property.Name}"))
            .ToArray();

        await Assert.That(forbidden).IsEmpty();
    }

    private static AwdpCheckerWork Work() => new(Guid.Parse("11111111-1111-1111-1111-111111111111"), RuntimeProvider.Docker, "checker:latest", ["/checker"], new Dictionary<string, string>(), new ContainerReceipt(Guid.Parse("11111111-1111-1111-1111-111111111111"), RuntimeProvider.Docker, "target-resource", RuntimeStatus.Running, new Dictionary<int, int>(), null, "target.internal", NetworkId: "network-1", RuntimeInstanceId: Guid.Parse("11111111-1111-1111-1111-111111111111")), "target.internal", 30, new Uri("https://api.example/api/internal/v1/awdp/fix-results"), "callback-token", TimeSpan.FromSeconds(20), FixInputEnabled: true);

    private sealed class RecordingCatalog(IOneShotJobRunner runner) : IOneShotRuntimeProviderCatalog
    {
        public IOneShotJobRunner OneShot(RuntimeProvider provider) => runner;
        public IAttachedOneShotJobRunner Attached(RuntimeProvider provider) => new TestAttachedJobRunner(runner);
    }

    private sealed class RecordingOneShotRunner : IOneShotJobRunner
    {
        public int InputCalls { get; private set; }
        public ContainerRequest? Request { get; private set; }
        public OneShotInputArchive? Input { get; private set; }
        public Exception? InputFailure { get; init; }
        public bool WaitForInputCancellation { get; init; }

        public async Task<OneShotResult> RunAsync(
            ContainerRequest request,
            OneShotInputArchive? input,
            CancellationToken cancellationToken)
        {
            InputCalls++;
            Request = request;
            Input = input;
            if (InputFailure is not null)
                throw InputFailure;
            if (WaitForInputCancellation)
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return Result();
        }

        private static OneShotResult Result()
        {
            var now = DateTimeOffset.UtcNow;
            return new("checker", 0, string.Empty, string.Empty, now, now);
        }
    }
}
