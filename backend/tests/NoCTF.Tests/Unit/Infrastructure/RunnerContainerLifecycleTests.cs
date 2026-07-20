using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using NoCTF.Application.Runtime.Ports;
using NoCTF.Domain.Runtime;
using NoCTF.Infrastructure.Runtime;

namespace NoCTF.Tests.Unit.Infrastructure;

public class RunnerContainerLifecycleTests
{
    [Test]
    public async Task GetAsync_ForwardsProviderInRunnerQuery()
    {
        var receipt = Receipt(RuntimeProvider.Kubernetes);
        var handler = new CapturingHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(receipt)
        });
        var runtime = CreateRuntime(handler);

        var result = await runtime.GetAsync(RuntimeProvider.Kubernetes, receipt.ResourceId, CancellationToken.None);

        await Assert.That(result).IsNotNull();
        await Assert.That(result!.OperationId).IsEqualTo(receipt.OperationId);
        await Assert.That(result.Provider).IsEqualTo(RuntimeProvider.Kubernetes);
        await Assert.That(result.ResourceId).IsEqualTo(receipt.ResourceId);
        await Assert.That(handler.RequestUri!.Query).Contains("Provider=Kubernetes");
        await Assert.That(handler.RunnerKey).IsEqualTo("runner-key");
    }

    [Test]
    public async Task CreateAsync_ForwardsProviderInRunnerBody()
    {
        var receipt = Receipt(RuntimeProvider.Kubernetes);
        var handler = new CapturingHandler(_ => new HttpResponseMessage(HttpStatusCode.Created)
        {
            Content = JsonContent.Create(receipt)
        });
        var runtime = CreateRuntime(handler);
        var request = new ContainerRequest(
            receipt.OperationId,
            RuntimeProvider.Kubernetes,
            "challenge:latest",
            [],
            new Dictionary<string, string>(),
            new Dictionary<string, string>(),
            new Dictionary<int, int>(),
            new(64 * 1024 * 1024, 100_000_000, 32),
            new(true, true, true, ["ALL"], []),
            TimeSpan.FromMinutes(10));

        await runtime.CreateAsync(request, CancellationToken.None);
        using var document = JsonDocument.Parse(handler.RequestBody!);

        await Assert.That(document.RootElement.GetProperty("provider").GetInt32())
            .IsEqualTo((int)RuntimeProvider.Kubernetes);
    }

    private static RunnerContainerLifecycle CreateRuntime(HttpMessageHandler handler)
    {
        var client = new HttpClient(handler) { BaseAddress = new Uri("http://runner/") };
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Runtime:Runner:ApiKey"] = "runner-key" })
            .Build();
        return new(client, configuration);
    }

    private static ContainerReceipt Receipt(RuntimeProvider provider) => new(
        Guid.NewGuid(),
        provider,
        "runtime-1",
        RuntimeStatus.Running,
        new Dictionary<int, int>(),
        "localhost",
        null);

    private sealed class CapturingHandler(Func<HttpRequestMessage, HttpResponseMessage> response) : HttpMessageHandler
    {
        public Uri? RequestUri { get; private set; }
        public string? RequestBody { get; private set; }
        public string? RunnerKey { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestUri = request.RequestUri;
            RunnerKey = request.Headers.GetValues("X-Runner-Key").Single();
            RequestBody = request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken);
            return response(request);
        }
    }
}
