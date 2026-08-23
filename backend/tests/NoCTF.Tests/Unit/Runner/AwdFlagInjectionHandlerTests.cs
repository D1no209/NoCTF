using System.Text.Json;
using Microsoft.Extensions.Configuration;
using NoCTF.Application.Messaging;
using NoCTF.Application.Runtime.Provisioning;
using NoCTF.Application.Runtime.Instances;
using NoCTF.Domain.Runtime;
using NoCTF.Runner.Composition;
using NoCTF.Runner.Messages;
using Wolverine.Attributes;

namespace NoCTF.Tests.Unit.Runner;

public sealed class AwdFlagInjectionHandlerTests
{
    [Test]
    public async Task Provider_handler_explicitly_opts_out_of_ambient_ef_transactions()
    {
        var attributes = typeof(AwdFlagInjectionHandler)
            .GetCustomAttributes(typeof(NonTransactionalAttribute), inherit: true);

        await Assert.That(attributes).HasSingleItem();
    }

    [Test]
    public async Task Nonzero_exit_retries_the_same_flag_on_the_original_node_queue()
    {
        var now = DateTimeOffset.Parse("2026-07-24T00:00:00Z");
        var work = CreateWork();
        var outbox = new RecordingOutbox();
        var handler = new AwdFlagInjectionHandler(
            new StubReader(work),
            new StubExecutor(new(17, false)),
            outbox,
            RunnerConfiguration().ToRunnerOptions(),
            new FixedTimeProvider(now));
        var message = CreateMessage(now.AddMinutes(1));

        var outcome = await handler.ExecuteAsync(message, CancellationToken.None);

        await Assert.That(outcome).IsEqualTo(MessageExecutionOutcome.DeferredSchedule);
        var retry = outbox.ScheduledNodes.Single();
        await Assert.That(retry.At).IsEqualTo(now.AddSeconds(1));
        await Assert.That(retry.Message).IsEqualTo(message with { FailedAttempts = 1 });
        await Assert.That(outbox.Published).IsEmpty();
    }

    [Test]
    public async Task Failure_without_an_available_retry_records_the_final_management_message()
    {
        var now = DateTimeOffset.Parse("2026-07-24T00:00:00Z");
        var outbox = new RecordingOutbox();
        var handler = new AwdFlagInjectionHandler(
            new StubReader(CreateWork()),
            new StubExecutor(new(-1, true)),
            outbox,
            RunnerConfiguration().ToRunnerOptions(),
            new FixedTimeProvider(now));

        var outcome = await handler.ExecuteAsync(
            CreateMessage(now.AddMilliseconds(500)),
            CancellationToken.None);

        await Assert.That(outcome).IsEqualTo(MessageExecutionOutcome.Applied);
        await Assert.That(outbox.ScheduledNodes).IsEmpty();
        await Assert.That(outbox.Published).HasSingleItem();
        await Assert.That(outbox.Published[0]).IsTypeOf<AwdFlagInjectionFailed>();
    }

    [Test]
    public async Task Compose_receipt_uses_compose_exec_and_expands_the_raw_template_outside_the_message()
    {
        var compose = new RecordingComposeRuntime();
        var catalog = new StubProviderCatalog(compose);
        var executor = new AwdFlagInjectionExecutor(catalog);
        var receipt = new ComposeReceipt(
            Guid.NewGuid(),
            RuntimeProvider.Docker,
            "project",
            "namespace",
            "localhost",
            1,
            DateTimeOffset.UtcNow);
        var work = CreateWork() with
        {
            RuntimeKind = RuntimeKind.Compose,
            ProviderReceiptJson = JsonSerializer.Serialize(receipt),
            Flag = "flag{a'b}",
            CommandTemplate = "set-flag ${FLAG}",
            ServiceName = "web"
        };

        var result = await executor.ExecuteAsync(work, CancellationToken.None);

        await Assert.That(result.ExitCode).IsEqualTo(0);
        await Assert.That(compose.ServiceName).IsEqualTo("web");
        await Assert.That(compose.Command).IsEquivalentTo([
            "/bin/sh", "-c", "set-flag flag{a'b}"]);
    }

    [Test]
    public async Task Expired_delivery_records_failure_without_calling_the_provider()
    {
        var now = DateTimeOffset.Parse("2026-07-24T00:01:00Z");
        var executor = new StubExecutor(new(0, false));
        var outbox = new RecordingOutbox();
        var handler = new AwdFlagInjectionHandler(
            new StubReader(CreateWork()),
            executor,
            outbox,
            RunnerConfiguration().ToRunnerOptions(),
            new FixedTimeProvider(now));

        await handler.ExecuteAsync(CreateMessage(now), CancellationToken.None);

        await Assert.That(executor.CallCount).IsEqualTo(0);
        await Assert.That(outbox.Published).HasSingleItem();
    }

    [Test]
    public async Task Provider_failure_rechecks_the_clock_before_scheduling()
    {
        var start = DateTimeOffset.Parse("2026-07-24T00:00:00Z");
        var deadline = start.AddSeconds(2);
        var clock = new MutableTimeProvider(start);
        var outbox = new RecordingOutbox();
        var handler = new AwdFlagInjectionHandler(
            new StubReader(CreateWork()),
            new CallbackExecutor(() => clock.UtcNow = deadline),
            outbox,
            RunnerConfiguration().ToRunnerOptions(),
            clock);

        await handler.ExecuteAsync(CreateMessage(deadline), CancellationToken.None);

        await Assert.That(outbox.ScheduledNodes).IsEmpty();
        var failure = outbox.Published.OfType<AwdFlagInjectionFailed>().Single();
        await Assert.That(failure.OccurredAt).IsEqualTo(deadline);
    }

    private static AwdFlagInjectionWork CreateWork() => new(
        Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
        RuntimeKind.Container,
        RuntimeProvider.Docker,
        "{}",
        "flag{value}",
        "set-flag ${FLAG}",
        null,
        TimeSpan.FromSeconds(30));

    private static InjectAwdFlag CreateMessage(DateTimeOffset validUntil) => new(
        Guid.Parse("11111111-1111-1111-1111-111111111111"),
        Guid.Parse("22222222-2222-2222-2222-222222222222"),
        Guid.Parse("33333333-3333-3333-3333-333333333333"),
        4,
        5,
        validUntil,
        "pool-a",
        "runner-a");

    private static IConfiguration RunnerConfiguration() => new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Runner:Pool"] = "pool-a",
            ["Runner:Id"] = "runner-a"
        })
        .Build();

    private sealed class StubReader(AwdFlagInjectionWork work) : IAwdFlagInjectionWorkReader
    {
        public Task<AwdFlagInjectionWork?> ReadAsync(
            InjectAwdFlag message,
            CancellationToken cancellationToken) => Task.FromResult<AwdFlagInjectionWork?>(work);
    }

    private sealed class StubExecutor(ContainerExecResult result) : IAwdFlagInjectionExecutor
    {
        public int CallCount { get; private set; }
        public Task<ContainerExecResult> ExecuteAsync(
            AwdFlagInjectionWork work,
            CancellationToken cancellationToken)
        {
            CallCount++;
            return Task.FromResult(result);
        }
    }

    private sealed class CallbackExecutor(Action callback) : IAwdFlagInjectionExecutor
    {
        public Task<ContainerExecResult> ExecuteAsync(
            AwdFlagInjectionWork work,
            CancellationToken cancellationToken)
        {
            callback();
            return Task.FromResult(new ContainerExecResult(1, false));
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class MutableTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset UtcNow { get; set; } = now;
        public override DateTimeOffset GetUtcNow() => UtcNow;
    }

    private sealed class RecordingOutbox : ITransactionalMessageOutbox
    {
        public List<object> Published { get; } = [];
        public List<(object Message, DateTimeOffset At)> ScheduledNodes { get; } = [];
        public ValueTask PublishAsync<T>(T message)
        {
            Published.Add(message!);
            return ValueTask.CompletedTask;
        }
        public ValueTask ScheduleAsync<T>(T message, DateTimeOffset scheduledAt) => ValueTask.CompletedTask;
        public ValueTask PublishToRunnerPoolAsync<T>(T message) where T : IRunnerPoolMessage => ValueTask.CompletedTask;
        public ValueTask ScheduleToRunnerPoolAsync<T>(T message, DateTimeOffset scheduledAt)
            where T : IRunnerPoolMessage => ValueTask.CompletedTask;
        public ValueTask PublishToRunnerNodeAsync<T>(T message) where T : IRunnerNodeMessage => ValueTask.CompletedTask;
        public ValueTask ScheduleToRunnerNodeAsync<T>(T message, DateTimeOffset scheduledAt)
            where T : IRunnerNodeMessage
        {
            ScheduledNodes.Add((message!, scheduledAt));
            return ValueTask.CompletedTask;
        }
        public Task FlushOutgoingMessagesAsync() => Task.CompletedTask;
    }

    private sealed class StubProviderCatalog(RecordingComposeRuntime compose) : IRuntimeProviderCatalog
    {
        public IContainerLifecycle Containers(RuntimeProvider provider) => throw new NotSupportedException();
        public IContainerSandboxLifecycle Sandbox(RuntimeProvider provider) => throw new NotSupportedException();
        public IComposeRuntime Compose(RuntimeProvider provider) => compose;

        public IOvaRuntime Appliance(RuntimeProvider provider) =>
            throw new NotSupportedException();
    }

    private sealed class RecordingComposeRuntime : IComposeRuntime
    {
        public string? ServiceName { get; private set; }
        public IReadOnlyList<string>? Command { get; private set; }
        public Task<ComposeReceipt> UpAsync(ComposeRequest request, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
        public Task DownAsync(ComposeReceipt receipt, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
        public Task<ComposeStatus?> GetStatusAsync(ComposeReceipt receipt, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
        public Task<ContainerExecResult> ExecAsync(
            ComposeReceipt receipt,
            string serviceName,
            IReadOnlyList<string> command,
            TimeSpan timeout,
            CancellationToken cancellationToken)
        {
            ServiceName = serviceName;
            Command = command;
            return Task.FromResult(new ContainerExecResult(0, false));
        }
    }
}
