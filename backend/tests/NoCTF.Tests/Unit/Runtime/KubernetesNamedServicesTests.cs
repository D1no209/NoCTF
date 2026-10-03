using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using k8s;
using NoCTF.Application.Runtime.Capacity;
using NoCTF.Application.Runtime.Provisioning;
using NoCTF.Domain.Runtime;
using NoCTF.Runtime.Kubernetes.Configuration;
using NoCTF.Runtime.Kubernetes.Containers;
using NoCTF.Runtime.Kubernetes.Services;

namespace NoCTF.Tests.Unit.Runtime;

public sealed class KubernetesNamedServicesTests
{
    [Test]
    [Arguments(1)]
    [Arguments(2)]
    public async Task Native_pods_use_discovery_only_for_multiple_services_and_replay_without_duplicates(int count)
    {
        using var api = new RecordingApi();
        using var client = new Kubernetes(new KubernetesClientConfiguration { Host = "http://runtime.test" }, [api]);
        var options = new KubernetesRuntimeOptions(Namespace: "runtime", PodPidsLimit: 256,
            ClusterDomain: "cluster.local", ClusterDnsServiceAddress: "10.96.0.10", ProtectedCidrs: ["10.0.0.0/8"],
            ImagePullSecrets: ["challenge-registry"]);
        var containers = new KubernetesContainerLifecycle(client, options);
        var runtime = new KubernetesContainerRuntime(client, containers, options);
        RuntimeServiceDefinition[] services = count == 1 ? [new("main", "nginx")] : [new("web", "nginx"), new("db", "alpine")];
        var request = new ContainerRuntimeRequest(Guid.NewGuid(), RuntimeProvider.Kubernetes, services, new Dictionary<string, string>(),
            RuntimeResourceBudgetPolicy.Sum(services.Select(service => service.Resources(256))), null, TimeSpan.FromSeconds(5),
            [new("http://{HOST}:{PORT}", RuntimeExposure.OwnerOnly, 80, services[0].Name)]);
        var receipt = await runtime.UpAsync(request, CancellationToken.None);
        var replay = await runtime.UpAsync(request, CancellationToken.None);
        var pods = api.Resources.Where(item => item.Key.Contains("/pods/", StringComparison.Ordinal)).Select(item => item.Value).ToArray();
        var discovery = api.Resources.Values.Where(value => value["spec"]?["clusterIP"]?.GetValue<string>() == "None").ToArray();
        await Assert.That(pods.Length).IsEqualTo(count);
        await Assert.That(discovery.Length).IsEqualTo(count > 1 ? 1 : 0);
        await Assert.That(replay.Services.Select(service => service.ResourceId)).IsEquivalentTo(receipt.Services.Select(service => service.ResourceId));
        foreach (var pod in pods)
        {
            await Assert.That(pod["spec"]!["containers"]!.AsArray().Count).IsEqualTo(1);
            await Assert.That(pod["spec"]!["imagePullSecrets"]![0]!["name"]!.GetValue<string>()).IsEqualTo("challenge-registry");
            await Assert.That(pod["spec"]!["containers"]![0]!["imagePullPolicy"]!.GetValue<string>()).IsEqualTo("Always");
            await Assert.That(pod["spec"]!["containers"]![0]!["securityContext"]).IsNull();
            await Assert.That(pod["spec"]!["containers"]![0]!["volumeMounts"]![0]!["mountPath"]!.GetValue<string>()).IsEqualTo("/noctf");
            await Assert.That(pod["spec"]!["securityContext"]!["fsGroup"]!.GetValue<long>()).IsEqualTo(65_532L);
            if (count > 1)
            {
                await Assert.That(pod["spec"]!["subdomain"]!.GetValue<string>()).IsEqualTo(request.ProjectName);
                await Assert.That(pod["spec"]!["dnsConfig"]!["searches"]![0]!.GetValue<string>()).IsEqualTo($"{request.ProjectName}.runtime.svc.cluster.local");
            }
        }
        var publicService = api.Resources.Values.Single(value => value["spec"]?["type"]?.GetValue<string>() == "NodePort");
        await Assert.That(publicService["spec"]!["selector"]!["noctf.io/runtime-id"]!.GetValue<string>()).IsEqualTo(receipt.Services[0].ResourceId);
        await runtime.DownAsync(receipt, RuntimeTerminationMode.Force, RuntimeTerminationPolicy.Default, CancellationToken.None);
        await Assert.That(api.Resources).IsEmpty();
    }

    private sealed class RecordingApi : DelegatingHandler
    {
        public Dictionary<string, JsonObject> Resources { get; } = new(StringComparer.Ordinal);
        private int podAddress;
        private readonly AsyncLocal<HttpRequestMessage?> currentRequest = new();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            currentRequest.Value = request;
            var path = request.RequestUri!.AbsolutePath;
            if (request.Method == HttpMethod.Get)
                return Resources.TryGetValue(path, out var resource) ? Response(HttpStatusCode.OK, resource) : Response(HttpStatusCode.NotFound, new() { ["kind"] = "Status", ["code"] = 404, ["reason"] = "NotFound" });
            if (request.Method == HttpMethod.Delete)
            {
                Resources.Remove(path, out var removed);
                return Response(HttpStatusCode.OK, path.Contains("/services/", StringComparison.Ordinal) ? removed ?? new JsonObject() : new() { ["kind"] = "Status", ["status"] = "Success" });
            }
            var body = JsonNode.Parse(await request.Content!.ReadAsStringAsync(cancellationToken))!.AsObject();
            var metadata = body["metadata"]!.AsObject();
            metadata["uid"] = Guid.NewGuid().ToString("D");
            var key = path + "/" + metadata["name"]!.GetValue<string>();
            if (Resources.ContainsKey(key)) return Response(HttpStatusCode.Conflict, new() { ["kind"] = "Status", ["code"] = 409, ["reason"] = "AlreadyExists" });
            if (path.EndsWith("/pods", StringComparison.Ordinal)) body["status"] = new JsonObject { ["phase"] = "Running", ["podIP"] = $"10.32.0.{++podAddress}" };
            if (body["spec"]?["type"]?.GetValue<string>() == "NodePort")
            {
                body["spec"]!["clusterIP"] = "10.96.0.42";
                foreach (var port in body["spec"]!["ports"]!.AsArray()) port!["nodePort"] = 32000 + port["port"]!.GetValue<int>();
            }
            Resources[key] = body;
            return Response(HttpStatusCode.Created, body);
        }

        private HttpResponseMessage Response(HttpStatusCode status, JsonObject body) => new(status)
        { RequestMessage = currentRequest.Value, Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json") };
    }
}
