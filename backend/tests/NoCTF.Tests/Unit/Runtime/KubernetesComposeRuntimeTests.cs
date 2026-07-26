using k8s;
using k8s.Autorest;
using k8s.Models;
using NSubstitute;
using NoCTF.Application.Runtime.Ports;
using NoCTF.Domain.Runtime;
using NoCTF.Runtime.Kubernetes.Compose;
using NoCTF.Runtime.Kubernetes.Configuration;

namespace NoCTF.Tests.Unit.Runtime;

public sealed class KubernetesComposeRuntimeTests
{
    [Test]
    public async Task Up_status_and_down_use_owned_platform_resources()
    {
        var client = Substitute.For<IKubernetes>();
        var apps = Substitute.For<IAppsV1Operations>();
        var core = Substitute.For<ICoreV1Operations>();
        var networking = Substitute.For<INetworkingV1Operations>();
        client.AppsV1.Returns(apps);
        client.CoreV1.Returns(core);
        client.NetworkingV1.Returns(networking);
        var converter = Substitute.For<IKomposeConverter>();
        converter.ConvertAsync(
                Arg.Any<string>(),
                Arg.Any<TimeSpan>(),
                Arg.Any<CancellationToken>())
            .Returns(SafeManifests);

        V1Deployment? deployment = null;
        var services = new List<V1Service>();
        V1NetworkPolicy? policy = null;
        apps.ReadNamespacedDeploymentWithHttpMessagesAsync(
                Arg.Any<string>(), Arg.Any<string>(), Arg.Any<bool?>(),
                Arg.Any<IReadOnlyDictionary<string, IReadOnlyList<string>>?>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromException<HttpOperationResponse<V1Deployment>>(NotFound()));
        core.ReadNamespacedServiceWithHttpMessagesAsync(
                Arg.Any<string>(), Arg.Any<string>(), Arg.Any<bool?>(),
                Arg.Any<IReadOnlyDictionary<string, IReadOnlyList<string>>?>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromException<HttpOperationResponse<V1Service>>(NotFound()));
        networking.ReadNamespacedNetworkPolicyWithHttpMessagesAsync(
                Arg.Any<string>(), Arg.Any<string>(), Arg.Any<bool?>(),
                Arg.Any<IReadOnlyDictionary<string, IReadOnlyList<string>>?>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromException<HttpOperationResponse<V1NetworkPolicy>>(NotFound()));
        apps.CreateNamespacedDeploymentWithHttpMessagesAsync(
                Arg.Do<V1Deployment>(value => deployment = value),
                Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<string?>(),
                Arg.Any<bool?>(),
                Arg.Any<IReadOnlyDictionary<string, IReadOnlyList<string>>?>(),
                Arg.Any<CancellationToken>())
            .Returns(call => Task.FromResult(new HttpOperationResponse<V1Deployment>
            {
                Body = call.Arg<V1Deployment>()!
            }));
        core.CreateNamespacedServiceWithHttpMessagesAsync(
                Arg.Do<V1Service>(services.Add),
                Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<string?>(),
                Arg.Any<bool?>(),
                Arg.Any<IReadOnlyDictionary<string, IReadOnlyList<string>>?>(),
                Arg.Any<CancellationToken>())
            .Returns(call => Task.FromResult(new HttpOperationResponse<V1Service>
            {
                Body = call.Arg<V1Service>()!
            }));
        networking.CreateNamespacedNetworkPolicyWithHttpMessagesAsync(
                Arg.Do<V1NetworkPolicy>(value => policy = value),
                Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<string?>(),
                Arg.Any<bool?>(),
                Arg.Any<IReadOnlyDictionary<string, IReadOnlyList<string>>?>(),
                Arg.Any<CancellationToken>())
            .Returns(call => Task.FromResult(new HttpOperationResponse<V1NetworkPolicy>
            {
                Body = call.Arg<V1NetworkPolicy>()!
            }));
        apps.ListNamespacedDeploymentWithHttpMessagesAsync(
                Arg.Any<string>(), Arg.Any<bool?>(), Arg.Any<string?>(), Arg.Any<string?>(),
                Arg.Any<string?>(), Arg.Any<int?>(), Arg.Any<string?>(), Arg.Any<string?>(),
                Arg.Any<bool?>(), Arg.Any<int?>(), Arg.Any<bool?>(), Arg.Any<bool?>(),
                Arg.Any<IReadOnlyDictionary<string, IReadOnlyList<string>>?>(),
                Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                if (deployment is not null)
                    deployment.Status = new V1DeploymentStatus { AvailableReplicas = 1 };
                return Task.FromResult(new HttpOperationResponse<V1DeploymentList>
                {
                    Body = new V1DeploymentList
                    {
                        Items = deployment is null ? [] : [deployment]
                    }
                });
            });
        core.ListNamespacedServiceWithHttpMessagesAsync(
                Arg.Any<string>(), Arg.Any<bool?>(), Arg.Any<string?>(), Arg.Any<string?>(),
                Arg.Any<string?>(), Arg.Any<int?>(), Arg.Any<string?>(), Arg.Any<string?>(),
                Arg.Any<bool?>(), Arg.Any<int?>(), Arg.Any<bool?>(), Arg.Any<bool?>(),
                Arg.Any<IReadOnlyDictionary<string, IReadOnlyList<string>>?>(),
                Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult(new HttpOperationResponse<V1ServiceList>
            {
                Body = new V1ServiceList { Items = services }
            }));
        networking.ListNamespacedNetworkPolicyWithHttpMessagesAsync(
                Arg.Any<string>(), Arg.Any<bool?>(), Arg.Any<string?>(), Arg.Any<string?>(),
                Arg.Any<string?>(), Arg.Any<int?>(), Arg.Any<string?>(), Arg.Any<string?>(),
                Arg.Any<bool?>(), Arg.Any<int?>(), Arg.Any<bool?>(), Arg.Any<bool?>(),
                Arg.Any<IReadOnlyDictionary<string, IReadOnlyList<string>>?>(),
                Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult(new HttpOperationResponse<V1NetworkPolicyList>
            {
                Body = new V1NetworkPolicyList
                {
                    Items = policy is null ? [] : [policy]
                }
            }));
        apps.DeleteNamespacedDeploymentWithHttpMessagesAsync(
                Arg.Any<string>(), Arg.Any<string>(), Arg.Any<V1DeleteOptions>(),
                Arg.Any<string?>(), Arg.Any<int?>(), Arg.Any<bool?>(), Arg.Any<bool?>(),
                Arg.Any<string?>(), Arg.Any<bool?>(),
                Arg.Any<IReadOnlyDictionary<string, IReadOnlyList<string>>?>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new HttpOperationResponse<V1Status>
            {
                Body = new V1Status()
            }));
        core.DeleteNamespacedServiceWithHttpMessagesAsync(
                Arg.Any<string>(), Arg.Any<string>(), Arg.Any<V1DeleteOptions>(),
                Arg.Any<string?>(), Arg.Any<int?>(), Arg.Any<bool?>(), Arg.Any<bool?>(),
                Arg.Any<string?>(), Arg.Any<bool?>(),
                Arg.Any<IReadOnlyDictionary<string, IReadOnlyList<string>>?>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new HttpOperationResponse<V1Service>
            {
                Body = new V1Service()
            }));
        networking.DeleteNamespacedNetworkPolicyWithHttpMessagesAsync(
                Arg.Any<string>(), Arg.Any<string>(), Arg.Any<V1DeleteOptions>(),
                Arg.Any<string?>(), Arg.Any<int?>(), Arg.Any<bool?>(), Arg.Any<bool?>(),
                Arg.Any<string?>(), Arg.Any<bool?>(),
                Arg.Any<IReadOnlyDictionary<string, IReadOnlyList<string>>?>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new HttpOperationResponse<V1Status>
            {
                Body = new V1Status()
            }));

        var runtime = new KubernetesComposeRuntime(
            client,
            new KubernetesRuntimeOptions(
                Namespace: "runtime",
                PublicHost: "node.example",
                PodPidsLimit: 512,
                ClusterDomain: "internal.example",
                ClusterDnsServiceAddress: "10.96.0.10"),
            converter);
        var request = Request();

        var receipt = await runtime.UpAsync(request, CancellationToken.None);
        var publicService = services.Single(service => service.Spec.Type == "NodePort");
        publicService.Spec.Ports.Single().NodePort = 31234;
        var status = await runtime.GetStatusAsync(receipt, CancellationToken.None);
        await runtime.DownAsync(receipt, CancellationToken.None);

        await Assert.That(receipt.Namespace).IsEqualTo("runtime");
        await Assert.That(policy).IsNotNull();
        await Assert.That(deployment).IsNotNull();
        await Assert.That(status!.Status).IsEqualTo(RuntimeStatus.Running);
        var web = status.Services.Single();
        await Assert.That(web.PublishedPorts[8080]).IsEqualTo(31234);
        await Assert.That(web.InternalHost).IsEqualTo(
            "web.rt-11111111111111111111111111111111.runtime.svc.internal.example");
        await Assert.That(apps.ReceivedCalls().Any(call =>
            call.GetMethodInfo().Name == "DeleteNamespacedDeploymentWithHttpMessagesAsync"))
            .IsTrue();
        await Assert.That(core.ReceivedCalls().Count(call =>
            call.GetMethodInfo().Name == "DeleteNamespacedServiceWithHttpMessagesAsync"))
            .IsEqualTo(2);
        await Assert.That(networking.ReceivedCalls().Any(call =>
            call.GetMethodInfo().Name == "DeleteNamespacedNetworkPolicyWithHttpMessagesAsync"))
            .IsTrue();
    }

    private static ComposeRequest Request() => new(
        Guid.Parse("11111111-1111-1111-1111-111111111111"),
        RuntimeProvider.Kubernetes,
        3,
        "display-project",
        """
        services:
          web:
            image: registry.example/web:v1
        """,
        new Dictionary<string, string>(),
        new Dictionary<string, string>(),
        new Dictionary<string, RuntimeResourceLimits>
        {
            ["web"] = new(268_435_456, 500_000_000, 0)
        },
        new(268_435_456, 500_000_000, 0),
        TimeSpan.FromHours(1),
        TimeSpan.FromSeconds(5),
        [
            new RuntimeUrlBinding(
                "http://{HOST}:{PORT}",
                RuntimeExposure.Participants,
                ContainerPort: 8080,
                ServiceName: "web")
        ]);

    private static HttpOperationException NotFound() => new("not found")
    {
        Response = new HttpResponseMessageWrapper(
            new HttpResponseMessage(System.Net.HttpStatusCode.NotFound),
            string.Empty)
    };

    private const string SafeManifests = """
        apiVersion: apps/v1
        kind: Deployment
        metadata:
          name: web
          labels:
            io.kompose.service: web
        spec:
          replicas: 1
          selector:
            matchLabels:
              io.kompose.service: web
          template:
            metadata:
              labels:
                io.kompose.service: web
            spec:
              containers:
                - name: web
                  image: registry.example/web:v1
        """;
}
