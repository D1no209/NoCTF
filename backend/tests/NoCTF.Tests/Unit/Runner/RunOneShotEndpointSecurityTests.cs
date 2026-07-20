using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
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

        var response = await client.PostAsJsonAsync("/jobs/one-shot", new CreateContainerRequest
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
