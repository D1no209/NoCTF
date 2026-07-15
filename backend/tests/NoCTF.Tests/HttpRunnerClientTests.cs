using System.Net;
using System.Net.Http.Json;
using NoCTF.PluginBase;
using NoCTF.Runner.Client;

namespace NoCTF.Tests;

public class HttpRunnerClientTests
{
    [Fact]
    public async Task CreateContainerAsync_AddsOperationIdWhenCallerOmitsIt()
    {
        ContainerConfig? captured = null;
        using var httpClient = new HttpClient(new DelegateHandler(async request =>
        {
            captured = await request.Content!.ReadFromJsonAsync<ContainerConfig>(
                cancellationToken: TestContext.Current.CancellationToken);
            return JsonResponse(CreateContainer());
        }))
        {
            BaseAddress = new Uri("http://runner.test")
        };
        var client = new HttpRunnerClient(httpClient);

        await client.CreateContainerAsync(
            new ContainerConfig("registry.test/challenge:latest"),
            TestContext.Current.CancellationToken);

        Assert.NotNull(captured);
        Assert.NotNull(captured.OperationId);
    }

    [Fact]
    public async Task ComposeUpAsync_PreservesCallerOperationId()
    {
        var operationId = Guid.NewGuid();
        ComposeConfig? captured = null;
        using var httpClient = new HttpClient(new DelegateHandler(async request =>
        {
            captured = await request.Content!.ReadFromJsonAsync<ComposeConfig>(
                cancellationToken: TestContext.Current.CancellationToken);
            return JsonResponse(new ComposeDeployment(
                Guid.NewGuid(), Guid.NewGuid(), null, null, "docker-compose", "noctf-test", "services: {}",
                "running", DateTime.UtcNow));
        }))
        {
            BaseAddress = new Uri("http://runner.test")
        };
        var client = new HttpRunnerClient(httpClient);

        await client.ComposeUpAsync(
            new ComposeConfig("noctf-test", "services: {}", OperationId: operationId),
            TestContext.Current.CancellationToken);

        Assert.NotNull(captured);
        Assert.Equal(operationId, captured.OperationId);
    }

    [Fact]
    public async Task CreateContainerAsync_RewritesRunnerLoopbackToRemoteRunnerHost()
    {
        using var httpClient = new HttpClient(new DelegateHandler(_ => Task.FromResult(JsonResponse(
            CreateContainer() with
            {
                InternalHost = "127.0.0.1",
                InternalPortMappings = new Dictionary<int, int> { [8080] = 32080 }
            }))))
        {
            BaseAddress = new Uri("http://runner.internal:8080")
        };
        var client = new HttpRunnerClient(httpClient);

        var result = await client.CreateContainerAsync(
            new ContainerConfig("registry.test/challenge:latest"),
            TestContext.Current.CancellationToken);

        Assert.Equal("runner.internal", result.InternalHost);
        Assert.Equal(32080, result.InternalPortMappings![8080]);
    }

    [Fact]
    public async Task GetComposeStatusAsync_RewritesRunnerLoopbackToRemoteRunnerHost()
    {
        var status = new ComposeStatus("noctf-test", "running",
        [
            new ComposeServiceInstance(
                "hill",
                "container-id",
                "running",
                null,
                new Dictionary<int, int> { [8080] = 32080 },
                InternalHost: "localhost",
                InternalPortMappings: new Dictionary<int, int> { [8080] = 32080 })
        ]);
        using var httpClient = new HttpClient(new DelegateHandler(_ => Task.FromResult(JsonResponse(status))))
        {
            BaseAddress = new Uri("https://runner.example.test")
        };
        var client = new HttpRunnerClient(httpClient);

        var result = await client.GetComposeStatusAsync(
            "noctf-test",
            ct: TestContext.Current.CancellationToken);

        Assert.Equal("runner.example.test", Assert.Single(result.Services).InternalHost);
    }

    private static HttpResponseMessage JsonResponse<T>(T value)
        => new(HttpStatusCode.OK) { Content = JsonContent.Create(value) };

    private static ContainerInstance CreateContainer()
        => new(
            Guid.NewGuid(), Guid.NewGuid(), null, null, "docker", "container-id", [], "running", DateTime.UtcNow);

    private sealed class DelegateHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> handler) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
            => handler(request);
    }
}
