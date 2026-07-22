using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using NoCTF.Application.Runtime.Ports;
using NoCTF.Domain.Runtime;
using NoCTF.Runner.Composition;
using NoCTF.Runner.Endpoints;

namespace NoCTF.Tests.Unit.Runner;

public sealed class RunOneShotEndpointSecurityTests
{
    [Test]
    public async Task Completed_job_callback_is_not_reclassified_when_execution_timeout_budget_elapses()
    {
        var callbackHandler = new CallbackHandler(TimeSpan.FromMilliseconds(1100));
        await using var factory = new WebApplicationFactory<RunnerProgramMarker>()
            .WithWebHostBuilder(builder =>
            {
                builder.ConfigureAppConfiguration(configuration => configuration.AddInMemoryCollection(
                    new Dictionary<string, string?>
                    {
                        ["Runtime:Runner:ApiKey"] = "test-runner-key",
                        ["RunnerScoring:CallbackBaseUrl"] = "https://api.example.test/",
                        ["RunnerScoring:SigningKey"] = "runner-completed-callback-signing-key-32-bytes",
                        ["RunnerScoring:Issuer"] = "NoCTF.Runner",
                        ["RunnerScoring:Audience"] = "NoCTF.ScoringInput"
                    }));
                builder.ConfigureTestServices(services =>
                {
                    services.RemoveAll<IOneShotRuntimeProviderCatalog>();
                    services.AddSingleton<IOneShotRuntimeProviderCatalog>(new CompletedCatalog());
                    services.AddHttpClient(nameof(RunnerScoringCallbackDispatcher))
                        .ConfigurePrimaryHttpMessageHandler(() => callbackHandler);
                });
            });
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Runner-Key", "test-runner-key");

        var response = await client.PostAsJsonAsync("/jobs/one-shot", new RunOneShotRequest
        {
            OperationId = Guid.NewGuid(),
            Provider = RuntimeProvider.Docker,
            Image = "checker-image",
            TimeoutSeconds = 1,
            ScoringCallback = new(new Uri("https://api.example.test/internal/check"), "runner-1",
                new Dictionary<string, string> { ["sourceKey"] = "awd:completed" })
        });

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        using var payload = JsonDocument.Parse(callbackHandler.Payload!);
        await Assert.That(payload.RootElement.GetProperty("timedOut").GetBoolean()).IsFalse();
        await Assert.That(payload.RootElement.GetProperty("exitCode").GetInt32()).IsEqualTo(0);
    }

    [Test]
    public async Task Checker_timeout_cleanup_failure_does_not_mask_timeout_callback_fact()
    {
        var callbackHandler = new CallbackHandler();
        await using var factory = new WebApplicationFactory<RunnerProgramMarker>()
            .WithWebHostBuilder(builder =>
            {
                builder.ConfigureAppConfiguration(configuration => configuration.AddInMemoryCollection(
                    new Dictionary<string, string?>
                    {
                        ["Runtime:Runner:ApiKey"] = "test-runner-key",
                        ["RunnerScoring:CallbackBaseUrl"] = "https://api.example.test/",
                        ["RunnerScoring:SigningKey"] = "runner-timeout-callback-signing-key-32-bytes",
                        ["RunnerScoring:Issuer"] = "NoCTF.Runner",
                        ["RunnerScoring:Audience"] = "NoCTF.ScoringInput"
                    }));
                builder.ConfigureTestServices(services =>
                {
                    services.RemoveAll<IOneShotRuntimeProviderCatalog>();
                    services.AddSingleton<IOneShotRuntimeProviderCatalog>(new TimeoutCatalog());
                    services.AddHttpClient(nameof(RunnerScoringCallbackDispatcher))
                        .ConfigurePrimaryHttpMessageHandler(() => callbackHandler);
                });
            });
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Runner-Key", "test-runner-key");

        var response = await client.PostAsJsonAsync("/jobs/one-shot", new RunOneShotRequest
        {
            OperationId = Guid.NewGuid(),
            Provider = RuntimeProvider.Docker,
            Image = "checker-image",
            TimeoutSeconds = 1,
            ScoringCallback = new(new Uri("https://api.example.test/internal/check"), "runner-1",
                new Dictionary<string, string> { ["sourceKey"] = "awd:timeout" })
        });

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(callbackHandler.AuthorizationScheme).IsEqualTo("Bearer");
        using var payload = JsonDocument.Parse(callbackHandler.Payload!);
        await Assert.That(payload.RootElement.GetProperty("timedOut").GetBoolean()).IsTrue();
        await Assert.That(payload.RootElement.GetProperty("exitCode").GetInt32()).IsEqualTo(-1);
        await Assert.That(payload.RootElement.TryGetProperty("standardOutput", out _)).IsFalse();
        await Assert.That(payload.RootElement.TryGetProperty("standardError", out _)).IsFalse();
    }

    [Test]
    public async Task Container_provider_failure_does_not_expose_sensitive_exception_in_response_or_logs()
    {
        const string sensitive = "FLAG{container-secret} environment-secret";
        var logs = new CapturingLoggerProvider();
        await using var factory = new WebApplicationFactory<RunnerProgramMarker>()
            .WithWebHostBuilder(builder =>
            {
                builder.ConfigureAppConfiguration(configuration => configuration.AddInMemoryCollection(
                    new Dictionary<string, string?> { ["Runtime:Runner:ApiKey"] = "test-runner-key" }));
                builder.ConfigureTestServices(services =>
                {
                    services.RemoveAll<IContainerRuntimeProviderCatalog>();
                    services.AddSingleton<IContainerRuntimeProviderCatalog>(new ThrowingContainerCatalog(sensitive));
                    services.AddSingleton<ILoggerProvider>(logs);
                });
            });
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Runner-Key", "test-runner-key");

        var response = await client.PostAsJsonAsync("/containers", new CreateContainerRequest
        {
            Provider = RuntimeProvider.Docker,
            Image = "challenge-image",
            Environment = new() { ["NOCTF_STAGE_1_FLAG"] = "redacted-at-boundary" }
        });
        var body = await response.Content.ReadAsStringAsync();

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.BadGateway);
        await Assert.That(body).DoesNotContain(sensitive);
        await Assert.That(string.Join(Environment.NewLine, logs.Entries)).DoesNotContain(sensitive);
    }

    [Test]
    public async Task Provider_failure_does_not_expose_sensitive_exception_in_response_or_logs()
    {
        const string sensitive = "FLAG{runner-secret} archive-content-secret";
        var logs = new CapturingLoggerProvider();
        await using var factory = new WebApplicationFactory<RunnerProgramMarker>()
            .WithWebHostBuilder(builder =>
            {
                builder.ConfigureAppConfiguration(configuration => configuration.AddInMemoryCollection(
                    new Dictionary<string, string?> { ["Runtime:Runner:ApiKey"] = "test-runner-key" }));
                builder.ConfigureTestServices(services =>
                {
                    services.RemoveAll<IOneShotRuntimeProviderCatalog>();
                    services.AddSingleton<IOneShotRuntimeProviderCatalog>(new ThrowingCatalog(sensitive));
                    services.AddSingleton<ILoggerProvider>(logs);
                });
            });
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Runner-Key", "test-runner-key");

        var response = await client.PostAsJsonAsync("/jobs/one-shot", new RunOneShotRequest
        {
            Provider = RuntimeProvider.Docker,
            Image = "verifier-image",
            Command = ["verify"],
            Environment = new() { ["FLAG"] = "redacted-at-boundary" }
        });
        var body = await response.Content.ReadAsStringAsync();

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.BadGateway);
        await Assert.That(body).DoesNotContain(sensitive);
        await Assert.That(string.Join(Environment.NewLine, logs.Entries)).DoesNotContain(sensitive);
    }

    private sealed class ThrowingCatalog(string message) : IOneShotRuntimeProviderCatalog
    {
        public IOneShotJobRunner OneShot(RuntimeProvider provider) => new ThrowingRunner(message);
    }

    private sealed class TimeoutCatalog : IOneShotRuntimeProviderCatalog
    {
        public IOneShotJobRunner OneShot(RuntimeProvider provider) => new TimeoutRunner();
    }

    private sealed class CompletedCatalog : IOneShotRuntimeProviderCatalog
    {
        public IOneShotJobRunner OneShot(RuntimeProvider provider) => new CompletedRunner();
    }

    private sealed class CompletedRunner : IOneShotJobRunner
    {
        public Task<OneShotResult> RunAsync(ContainerRequest request, CancellationToken cancellationToken)
        {
            var now = DateTimeOffset.UtcNow;
            return Task.FromResult(new OneShotResult("job", 0, string.Empty, string.Empty, now, now));
        }
    }

    private sealed class TimeoutRunner : IOneShotJobRunner
    {
        public async Task<OneShotResult> RunAsync(ContainerRequest request, CancellationToken cancellationToken)
        {
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                throw new InvalidOperationException("The timeout runner unexpectedly resumed.");
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw new InvalidOperationException("Simulated provider cleanup failure after timeout.");
            }
        }
    }

    private sealed class CallbackHandler(TimeSpan delay = default) : HttpMessageHandler
    {
        public string? Payload { get; private set; }
        public string? AuthorizationScheme { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (delay > TimeSpan.Zero) await Task.Delay(delay, cancellationToken);
            AuthorizationScheme = request.Headers.Authorization?.Scheme;
            Payload = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK);
        }
    }

    private sealed class ThrowingContainerCatalog(string message) : IContainerRuntimeProviderCatalog
    {
        public IContainerLifecycle Containers(RuntimeProvider provider) => new ThrowingContainer(message);
    }

    private sealed class ThrowingContainer(string message) : IContainerLifecycle
    {
        public Task<ContainerReceipt> CreateAsync(ContainerRequest request, CancellationToken cancellationToken) =>
            throw new InvalidOperationException(message);
        public Task DestroyAsync(ContainerReceipt receipt, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<ContainerReceipt?> GetAsync(RuntimeProvider provider, string resourceId, CancellationToken cancellationToken) =>
            Task.FromResult<ContainerReceipt?>(null);
    }

    private sealed class ThrowingRunner(string message) : IOneShotJobRunner
    {
        public Task<OneShotResult> RunAsync(ContainerRequest request, CancellationToken cancellationToken) =>
            throw new InvalidOperationException(message);
    }

    private sealed class CapturingLoggerProvider : ILoggerProvider
    {
        public ConcurrentQueue<string> Entries { get; } = [];
        public ILogger CreateLogger(string categoryName) => new CapturingLogger(Entries);
        public void Dispose() { }

        private sealed class CapturingLogger(ConcurrentQueue<string> entries) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
            public bool IsEnabled(LogLevel logLevel) => true;
            public void Log<TState>(
                LogLevel logLevel,
                EventId eventId,
                TState state,
                Exception? exception,
                Func<TState, Exception?, string> formatter)
            {
                entries.Enqueue(formatter(state, exception));
                if (exception is not null)
                    entries.Enqueue(exception.ToString());
            }
        }
    }
}
