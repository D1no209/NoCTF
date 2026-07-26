using k8s;
using k8s.Models;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using NoCTF.Application.Runtime.Ports;
using NoCTF.Runtime.Kubernetes.Configuration;
using NoCTF.Runtime.Kubernetes.Networking;

namespace NoCTF.Runtime.Kubernetes.Compose;

public sealed record KubernetesComposeManifests(
    IReadOnlyList<V1Deployment> Deployments,
    IReadOnlyList<V1Service> Services);

public sealed record KubernetesComposePlan(
    string RuntimeName,
    IReadOnlyList<V1Deployment> Deployments,
    IReadOnlyList<V1Service> Services,
    V1NetworkPolicy NetworkPolicy);

public static class KubernetesComposeManifestPolicy
{
    private const string KomposeServiceLabel = "io.kompose.service";
    public const string ComposeServiceLabel = "noctf.io/compose-service";
    public const string ResourceRoleLabel = "noctf.io/resource-role";

    public static KubernetesComposeManifests ParseAndValidate(
        string yaml,
        IReadOnlySet<string> expectedServices)
    {
        IReadOnlyList<object> resources;
        try
        {
            resources = KubernetesYaml.LoadAllFromString(yaml, strict: true);
        }
        catch (Exception exception)
        {
            throw new InvalidOperationException(
                "Kompose produced invalid Kubernetes manifests.",
                exception);
        }

        var deployments = resources.OfType<V1Deployment>().ToArray();
        var services = resources.OfType<V1Service>().ToArray();
        var errors = resources
            .Where(resource => resource is not (V1Deployment or V1Service))
            .Select(resource =>
                $"Kompose resource '{ResourceKind(resource)}' is not supported.")
            .ToList();

        foreach (var deployment in deployments)
            ValidateDeployment(deployment, expectedServices, errors);
        foreach (var service in services)
            ValidateService(service, expectedServices, errors);
        foreach (var expectedService in expectedServices)
        {
            if (deployments.Count(deployment =>
                    string.Equals(
                        ServiceName(deployment.Metadata?.Labels),
                        expectedService,
                        StringComparison.Ordinal)) != 1)
                errors.Add(
                    $"Compose service '{expectedService}' did not produce one Deployment.");
            if (services.Count(service =>
                    string.Equals(
                        ServiceName(service.Metadata?.Labels),
                        expectedService,
                        StringComparison.Ordinal)) > 1)
                errors.Add(
                    $"Compose service '{expectedService}' produced multiple Services.");
        }

        if (errors.Count > 0)
            throw new InvalidOperationException(string.Join(" ", errors));
        return new(deployments, services);
    }

    public static KubernetesComposePlan ApplyPlatformPolicy(
        KubernetesComposeManifests manifests,
        ComposeRequest request,
        KubernetesRuntimeOptions options)
    {
        if (!options.NetworkPolicyRequired)
            throw new InvalidOperationException(
                "Kubernetes Compose requires NetworkPolicy enforcement.");
        if (options.PodPidsLimit <= 0)
            throw new InvalidOperationException(
                "Kubernetes PodPidsLimit must be positive.");
        if (string.IsNullOrWhiteSpace(options.ClusterDomain))
            throw new InvalidOperationException(
                "Kubernetes ClusterDomain is required.");

        var runtimeName = $"rt-{request.OperationId:N}";
        var runtimeSelector = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["noctf.io/managed"] = "true",
            ["noctf.io/runtime-instance-id"] = request.OperationId.ToString("D"),
            ["noctf.io/generation"] = request.Generation.ToString(CultureInfo.InvariantCulture)
        };
        var publicPorts = (request.UrlBindings ?? [])
            .GroupBy(binding => binding.ServiceName!, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group
                    .Select(binding => binding.ContainerPort!.Value)
                    .Distinct()
                    .Order()
                    .ToArray(),
                StringComparer.Ordinal);
        var controlPorts = request.ControlCheckUrlBinding is { } control
            ? new Dictionary<string, int[]>(StringComparer.Ordinal)
            {
                [control.ServiceName!] = [control.ContainerPort!.Value]
            }
            : new Dictionary<string, int[]>(StringComparer.Ordinal);

        var deployments = new List<V1Deployment>(manifests.Deployments.Count);
        foreach (var deployment in manifests.Deployments)
        {
            var serviceName = ServiceName(deployment.Metadata.Labels)
                ?? throw new InvalidOperationException(
                    "Kompose Deployment does not identify a Compose service.");
            var selector = new Dictionary<string, string>(runtimeSelector, StringComparer.Ordinal)
            {
                [ComposeServiceLabel] = serviceName
            };
            var labels = PlatformLabels(request, selector, "workload");
            var pod = deployment.Spec.Template.Spec;
            var container = pod.Containers.Single();
            var limits = request.ServiceResources[serviceName];
            container.Name = serviceName;
            container.ImagePullPolicy = options.ImagePullPolicy;
            container.Resources = ResourceRequirements(limits);
            container.SecurityContext = new V1SecurityContext
            {
                AllowPrivilegeEscalation = false,
                Privileged = false,
                Capabilities = new V1Capabilities { Drop = ["ALL"], Add = [] },
                SeccompProfile = new V1SeccompProfile { Type = "RuntimeDefault" }
            };
            pod.AutomountServiceAccountToken = false;
            pod.EnableServiceLinks = false;
            pod.Hostname = serviceName;
            pod.Subdomain = runtimeName;
            pod.DnsPolicy = "ClusterFirst";
            pod.DnsConfig = new V1PodDNSConfig
            {
                Searches =
                [
                    $"{runtimeName}.{options.Namespace}.svc.{options.ClusterDomain}"
                ]
            };
            pod.ServiceAccount = null;
            pod.ServiceAccountName = null;
            pod.NodeName = null;
            pod.NodeSelector = null;
            pod.RuntimeClassName = null;
            pod.HostAliases = null;
            pod.HostNetwork = false;
            pod.HostPID = false;
            pod.HostIPC = false;
            pod.ShareProcessNamespace = false;
            pod.Volumes = null;
            pod.InitContainers = null;

            deployment.Metadata = new V1ObjectMeta
            {
                Name = ResourceName(runtimeName, serviceName, "workload"),
                NamespaceProperty = options.Namespace,
                Labels = labels,
                Annotations = PlatformAnnotations(request)
            };
            deployment.Spec.Replicas = 1;
            deployment.Spec.Selector = new V1LabelSelector { MatchLabels = selector };
            deployment.Spec.Template.Metadata = new V1ObjectMeta { Labels = labels };
            deployment.Spec.Strategy = new V1DeploymentStrategy { Type = "Recreate" };
            deployments.Add(deployment);
        }

        var services = new List<V1Service>
        {
            new()
            {
                Metadata = new V1ObjectMeta
                {
                    Name = runtimeName,
                    NamespaceProperty = options.Namespace,
                    Labels = PlatformLabels(request, runtimeSelector, "dns"),
                    Annotations = PlatformAnnotations(request)
                },
                Spec = new V1ServiceSpec
                {
                    ClusterIP = "None",
                    PublishNotReadyAddresses = true,
                    Selector = runtimeSelector
                }
            }
        };
        foreach (var service in request.ServiceResources.Keys.Order(StringComparer.Ordinal))
        {
            if (!publicPorts.TryGetValue(service, out var ports) || ports.Length == 0)
                continue;
            var selector = new Dictionary<string, string>(runtimeSelector, StringComparer.Ordinal)
            {
                [ComposeServiceLabel] = service
            };
            services.Add(new V1Service
            {
                Metadata = new V1ObjectMeta
                {
                    Name = ResourceName(runtimeName, service, "public"),
                    NamespaceProperty = options.Namespace,
                    Labels = PlatformLabels(request, selector, "public"),
                    Annotations = PlatformAnnotations(request)
                },
                Spec = new V1ServiceSpec
                {
                    Selector = selector,
                    Type = "NodePort",
                    Ports = ports.Select(port => new V1ServicePort
                    {
                        Name = $"tcp-{port}",
                        Protocol = "TCP",
                        Port = port,
                        TargetPort = port
                    }).ToList()
                }
            });
        }

        var exposedPorts = publicPorts.Values
            .SelectMany(ports => ports)
            .Concat(controlPorts.Values.SelectMany(ports => ports))
            .Distinct()
            .Order()
            .ToArray();
        var ingress = new List<V1NetworkPolicyIngressRule>
        {
            new()
            {
                FromProperty =
                [
                    new V1NetworkPolicyPeer
                    {
                        PodSelector = new V1LabelSelector { MatchLabels = runtimeSelector }
                    }
                ]
            }
        };
        if (exposedPorts.Length > 0)
        {
            ingress.Add(new V1NetworkPolicyIngressRule
            {
                Ports = exposedPorts.Select(port => new V1NetworkPolicyPort
                {
                    Protocol = "TCP",
                    Port = port
                }).ToList()
            });
        }
        var networkPolicy = new V1NetworkPolicy
        {
            Metadata = new V1ObjectMeta
            {
                Name = $"{runtimeName}-network",
                NamespaceProperty = options.Namespace,
                Labels = PlatformLabels(request, runtimeSelector, "network-policy"),
                Annotations = PlatformAnnotations(request)
            },
            Spec = new V1NetworkPolicySpec
            {
                PodSelector = new V1LabelSelector { MatchLabels = runtimeSelector },
                PolicyTypes = ["Ingress", "Egress"],
                Ingress = ingress,
                Egress = KubernetesEgressPolicy.Build(
                    request.EgressPolicy,
                    new V1LabelSelector { MatchLabels = runtimeSelector },
                    options).ToList()
            }
        };
        return new(runtimeName, deployments, services, networkPolicy);
    }

    private static void ValidateDeployment(
        V1Deployment deployment,
        IReadOnlySet<string> expectedServices,
        ICollection<string> errors)
    {
        var name = deployment.Metadata?.Name ?? "<unnamed>";
        ValidateMetadata(deployment.Metadata, $"Deployment '{name}'", errors);
        var serviceName = ServiceName(deployment.Metadata?.Labels);
        if (serviceName is null || !expectedServices.Contains(serviceName))
            errors.Add($"Deployment '{name}' does not identify a known Compose service.");
        if (deployment.Spec?.Replicas is not (null or 1))
            errors.Add($"Deployment '{name}' must use exactly one replica.");
        if (deployment.Spec?.Template?.Spec is not { } pod)
        {
            errors.Add($"Deployment '{name}' requires a Pod spec.");
            return;
        }
        if (pod.HostNetwork == true || pod.HostPID == true || pod.HostIPC == true
            || pod.ShareProcessNamespace == true)
            errors.Add($"Deployment '{name}' cannot use host namespaces.");
        if (pod.Volumes?.Count > 0)
            errors.Add($"Deployment '{name}' cannot use volumes.");
        if (pod.InitContainers?.Count > 0)
            errors.Add($"Deployment '{name}' cannot use init containers.");
        if (!string.IsNullOrWhiteSpace(pod.ServiceAccount)
            || !string.IsNullOrWhiteSpace(pod.ServiceAccountName))
            errors.Add($"Deployment '{name}' cannot use a service account.");
        if (!string.IsNullOrWhiteSpace(pod.NodeName)
            || pod.NodeSelector?.Count > 0
            || !string.IsNullOrWhiteSpace(pod.RuntimeClassName))
            errors.Add($"Deployment '{name}' cannot control node placement or runtime class.");
        if (pod.HostAliases?.Count > 0 || pod.DnsConfig is not null)
            errors.Add($"Deployment '{name}' cannot override cluster DNS.");
        if (pod.Containers?.Count != 1)
        {
            errors.Add($"Deployment '{name}' must contain exactly one container.");
            return;
        }

        var container = pod.Containers[0];
        if (container.SecurityContext?.Privileged == true)
            errors.Add($"Deployment '{name}' cannot run privileged.");
        if (container.SecurityContext?.Capabilities?.Add?.Count > 0)
            errors.Add($"Deployment '{name}' cannot add capabilities.");
        if (string.Equals(
                container.SecurityContext?.ProcMount,
                "Unmasked",
                StringComparison.OrdinalIgnoreCase))
            errors.Add($"Deployment '{name}' cannot use an unmasked proc mount.");
        if (container.Ports?.Any(port => port.HostPort is > 0) == true)
            errors.Add($"Deployment '{name}' cannot publish host ports.");
        if (container.VolumeMounts?.Count > 0 || container.VolumeDevices?.Count > 0)
            errors.Add($"Deployment '{name}' cannot mount volumes.");
    }

    private static void ValidateService(
        V1Service service,
        IReadOnlySet<string> expectedServices,
        ICollection<string> errors)
    {
        var name = service.Metadata?.Name ?? "<unnamed>";
        ValidateMetadata(service.Metadata, $"Service '{name}'", errors);
        var serviceName = ServiceName(service.Metadata?.Labels);
        if (serviceName is null || !expectedServices.Contains(serviceName))
            errors.Add($"Service '{name}' does not identify a known Compose service.");
        var type = service.Spec?.Type;
        if (!string.IsNullOrWhiteSpace(type)
            && !string.Equals(type, "ClusterIP", StringComparison.OrdinalIgnoreCase))
            errors.Add($"Service '{name}' cannot use Service type '{type}'.");
        if (service.Spec?.ExternalIPs?.Count > 0
            || !string.IsNullOrWhiteSpace(service.Spec?.ExternalName)
            || !string.IsNullOrWhiteSpace(service.Spec?.LoadBalancerClass)
            || !string.IsNullOrWhiteSpace(service.Spec?.LoadBalancerIP)
            || service.Spec?.LoadBalancerSourceRanges?.Count > 0
            || service.Spec?.Ports?.Any(port => port.NodePort is > 0) == true)
            errors.Add($"Service '{name}' cannot request external exposure.");
    }

    private static void ValidateMetadata(
        V1ObjectMeta? metadata,
        string resource,
        ICollection<string> errors)
    {
        if (!string.IsNullOrWhiteSpace(metadata?.NamespaceProperty))
            errors.Add($"{resource} cannot set metadata.namespace.");
        if (!string.IsNullOrWhiteSpace(metadata?.GenerateName)
            || metadata?.OwnerReferences?.Count > 0
            || metadata?.Finalizers?.Count > 0)
            errors.Add($"{resource} cannot control generated names, owners, or finalizers.");
    }

    private static string? ServiceName(IDictionary<string, string>? labels) =>
        labels is not null && labels.TryGetValue(KomposeServiceLabel, out var serviceName)
            ? serviceName
            : null;

    private static string ResourceKind(object resource) =>
        resource is IKubernetesObject kubernetes
            ? kubernetes.Kind
            : resource.GetType().Name;

    private static IDictionary<string, string> PlatformLabels(
        ComposeRequest request,
        IReadOnlyDictionary<string, string> selector,
        string role)
    {
        var labels = new Dictionary<string, string>(request.Labels, StringComparer.Ordinal);
        foreach (var pair in selector)
            labels[pair.Key] = pair.Value;
        labels[ResourceRoleLabel] = role;
        return labels;
    }

    private static IDictionary<string, string> PlatformAnnotations(ComposeRequest request) =>
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["noctf.io/compose-project"] = request.ProjectName
        };

    private static V1ResourceRequirements ResourceRequirements(
        RuntimeResourceLimits limits)
    {
        var cpuMillis = checked((limits.NanoCpus + 999_999) / 1_000_000);
        var resources = new Dictionary<string, ResourceQuantity>
        {
            ["memory"] = new(limits.MemoryBytes.ToString(CultureInfo.InvariantCulture)),
            ["cpu"] = new($"{cpuMillis}m")
        };
        return new V1ResourceRequirements
        {
            Limits = resources,
            Requests = new Dictionary<string, ResourceQuantity>(resources)
        };
    }

    private static string ResourceName(
        string runtimeName,
        string serviceName,
        string role)
    {
        var hash = Convert.ToHexStringLower(
            SHA256.HashData(Encoding.UTF8.GetBytes($"{serviceName}:{role}")))[..8];
        var available = 63 - runtimeName.Length - hash.Length - 2;
        var component = serviceName[..Math.Min(serviceName.Length, available)]
            .TrimEnd('-');
        return $"{runtimeName}-{component}-{hash}";
    }
}
