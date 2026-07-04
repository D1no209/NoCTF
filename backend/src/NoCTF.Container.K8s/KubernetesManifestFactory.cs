using System.Text;
using k8s.Models;
using NoCTF.PluginBase;

namespace NoCTF.Container.K8s;

public static class KubernetesManifestFactory
{
    public const string ManagedByLabel = "app.kubernetes.io/managed-by";
    public const string InstanceLabel = "noctf.io/instance";
    public const string ProjectLabel = "noctf.io/project";
    public const string ServiceLabel = "noctf.io/service";

    public static Dictionary<string, string> CommonLabels(string component, string instance) => new()
    {
        [ManagedByLabel] = "noctf-runner",
        ["app.kubernetes.io/part-of"] = "noctf",
        ["app.kubernetes.io/component"] = component,
        [InstanceLabel] = KubernetesNames.SafeName(instance)
    };

    public static V1Namespace Namespace(string name, IReadOnlyDictionary<string, string>? labels = null)
        => new()
        {
            Metadata = new V1ObjectMeta
            {
                Name = name,
                Labels = MergeUserLabels(CommonLabels("challenge-instance", name), labels)
            }
        };

    public static V1ResourceQuota ResourceQuota(string namespaceName, KubernetesRunnerOptions options)
        => new()
        {
            Metadata = new V1ObjectMeta
            {
                Name = "noctf-quota",
                NamespaceProperty = namespaceName,
                Labels = CommonLabels("quota", namespaceName)
            },
            Spec = new V1ResourceQuotaSpec
            {
                Hard = new Dictionary<string, ResourceQuantity>
                {
                    ["limits.cpu"] = new(options.NamespaceCpuLimit),
                    ["limits.memory"] = new(options.NamespaceMemoryLimit),
                    ["pods"] = new(options.NamespacePodLimit)
                }
            }
        };

    public static V1LimitRange LimitRange(string namespaceName, KubernetesRunnerOptions options)
        => new()
        {
            Metadata = new V1ObjectMeta
            {
                Name = "noctf-limits",
                NamespaceProperty = namespaceName,
                Labels = CommonLabels("limits", namespaceName)
            },
            Spec = new V1LimitRangeSpec
            {
                Limits =
                [
                    new V1LimitRangeItem
                    {
                        Type = "Container",
                        DefaultProperty = new Dictionary<string, ResourceQuantity>
                        {
                            ["cpu"] = new(options.CpuLimit),
                            ["memory"] = new(options.MemoryLimit),
                            ["ephemeral-storage"] = new(options.EphemeralStorageLimit)
                        },
                        DefaultRequest = new Dictionary<string, ResourceQuantity>
                        {
                            ["cpu"] = new(options.CpuRequest),
                            ["memory"] = new(options.MemoryRequest)
                        }
                    }
                ]
            }
        };

    public static V1ConfigMap ConfigMapVolume(string namespaceName, KubernetesVolumeSpec volume)
        => new()
        {
            Metadata = new V1ObjectMeta
            {
                Name = KubernetesNames.SafeName(volume.Name),
                NamespaceProperty = namespaceName,
                Labels = CommonLabels("volume-configmap", namespaceName)
            },
            Data = new Dictionary<string, string>(volume.Data, StringComparer.Ordinal)
        };

    public static V1Secret SecretVolume(string namespaceName, KubernetesVolumeSpec volume)
        => new()
        {
            Metadata = new V1ObjectMeta
            {
                Name = KubernetesNames.SafeName(volume.Name),
                NamespaceProperty = namespaceName,
                Labels = CommonLabels("volume-secret", namespaceName)
            },
            Type = "Opaque",
            Data = volume.Data.ToDictionary(
                kvp => kvp.Key,
                kvp => Encoding.UTF8.GetBytes(kvp.Value),
                StringComparer.Ordinal)
        };

    public static IReadOnlyList<V1NetworkPolicy> NetworkPolicies(
        string namespaceName,
        OrchestrationNetworkMode networkMode)
    {
        var policies = new List<V1NetworkPolicy>
        {
            new()
            {
                Metadata = new V1ObjectMeta
                {
                    Name = "default-deny",
                    NamespaceProperty = namespaceName,
                    Labels = CommonLabels("network-policy", namespaceName)
                },
                Spec = new V1NetworkPolicySpec
                {
                    PodSelector = new V1LabelSelector(),
                    PolicyTypes = ["Ingress", "Egress"]
                }
            },
            new()
            {
                Metadata = new V1ObjectMeta
                {
                    Name = "allow-same-instance",
                    NamespaceProperty = namespaceName,
                    Labels = CommonLabels("network-policy", namespaceName)
                },
                Spec = new V1NetworkPolicySpec
                {
                    PodSelector = new V1LabelSelector(),
                    PolicyTypes = ["Ingress", "Egress"],
                    Ingress =
                    [
                        new V1NetworkPolicyIngressRule
                        {
                            FromProperty = [new V1NetworkPolicyPeer { PodSelector = new V1LabelSelector() }]
                        }
                    ],
                    Egress =
                    [
                        new V1NetworkPolicyEgressRule
                        {
                            To = [new V1NetworkPolicyPeer { PodSelector = new V1LabelSelector() }]
                        },
                        new V1NetworkPolicyEgressRule
                        {
                            Ports =
                            [
                                new V1NetworkPolicyPort { Port = 53, Protocol = "UDP" },
                                new V1NetworkPolicyPort { Port = 53, Protocol = "TCP" }
                            ]
                        }
                    ]
                }
            }
        };

        if (networkMode == OrchestrationNetworkMode.Open)
        {
            policies.Add(new V1NetworkPolicy
            {
                Metadata = new V1ObjectMeta
                {
                    Name = "allow-open-egress",
                    NamespaceProperty = namespaceName,
                    Labels = CommonLabels("network-policy", namespaceName)
                },
                Spec = new V1NetworkPolicySpec
                {
                    PodSelector = new V1LabelSelector(),
                    PolicyTypes = ["Egress"],
                    Egress =
                    [
                        new V1NetworkPolicyEgressRule
                        {
                            To =
                            [
                                new V1NetworkPolicyPeer
                                {
                                    IpBlock = new V1IPBlock { Cidr = "0.0.0.0/0" }
                                }
                            ]
                        }
                    ]
                }
            });
        }

        return policies;
    }

    public static V1NetworkPolicy ExposedIngressPolicy(string namespaceName, string name, int port)
        => new()
        {
            Metadata = new V1ObjectMeta
            {
                Name = KubernetesNames.SafeName($"allow-exposed-{name}-{port}"),
                NamespaceProperty = namespaceName,
                Labels = CommonLabels("network-policy", namespaceName)
            },
            Spec = new V1NetworkPolicySpec
            {
                PodSelector = new V1LabelSelector { MatchLabels = SelectorLabels(name) },
                PolicyTypes = ["Ingress"],
                Ingress =
                [
                    new V1NetworkPolicyIngressRule
                    {
                        FromProperty =
                        [
                            new V1NetworkPolicyPeer
                            {
                                IpBlock = new V1IPBlock { Cidr = "0.0.0.0/0" }
                            },
                            new V1NetworkPolicyPeer
                            {
                                NamespaceSelector = new V1LabelSelector()
                            }
                        ],
                        Ports =
                        [
                            new V1NetworkPolicyPort { Port = port, Protocol = "TCP" }
                        ]
                    }
                ]
            }
        };

    public static V1Deployment Deployment(
        string namespaceName,
        string name,
        ContainerConfig config,
        KubernetesRunnerOptions options,
        OrchestrationSpec spec,
        IReadOnlyDictionary<string, string>? extraLabels = null)
    {
        var labels = BuildWorkloadLabels(name, config.Labels, extraLabels);
        ApplyLabels(labels, spec.Kubernetes.Labels);
        var containerPort = ResolvePort(config, spec);

        return new V1Deployment
        {
            Metadata = new V1ObjectMeta
            {
                Name = name,
                NamespaceProperty = namespaceName,
                Labels = labels
            },
            Spec = new V1DeploymentSpec
            {
                Replicas = 1,
                Selector = new V1LabelSelector { MatchLabels = SelectorLabels(name) },
                Template = new V1PodTemplateSpec
                {
                    Metadata = new V1ObjectMeta { Labels = labels, Annotations = spec.Kubernetes.Annotations },
                    Spec = PodSpec(name, config, options, spec, containerPort)
                }
            }
        };
    }

    public static V1Job Job(
        string namespaceName,
        string name,
        ContainerConfig config,
        KubernetesRunnerOptions options,
        OrchestrationSpec spec)
    {
        var labels = BuildWorkloadLabels(name, config.Labels, null);
        ApplyLabels(labels, spec.Kubernetes.Labels);
        return new V1Job
        {
            Metadata = new V1ObjectMeta { Name = name, NamespaceProperty = namespaceName, Labels = labels },
            Spec = new V1JobSpec
            {
                BackoffLimit = 0,
                Template = new V1PodTemplateSpec
                {
                    Metadata = new V1ObjectMeta { Labels = labels },
                    Spec = PodSpec(name, config, options, spec, null, restartPolicy: "Never")
                }
            }
        };
    }

    public static V1Service Service(
        string namespaceName,
        string name,
        int port,
        OrchestrationExposureType exposure,
        IReadOnlyDictionary<string, string>? extraLabels = null)
        => Service(namespaceName, name, [port], exposure, extraLabels);

    public static V1Service Service(
        string namespaceName,
        string name,
        IReadOnlyList<int> ports,
        OrchestrationExposureType exposure,
        IReadOnlyDictionary<string, string>? extraLabels = null)
        => new()
        {
            Metadata = new V1ObjectMeta
            {
                Name = name,
                NamespaceProperty = namespaceName,
                Labels = MergeSystemLabels(BuildWorkloadLabels(name, null, extraLabels), extraLabels)
            },
            Spec = new V1ServiceSpec
            {
                Type = exposure == OrchestrationExposureType.NodePort ? "NodePort" : "ClusterIP",
                Selector = SelectorLabels(name),
                Ports = ports
                    .Where(port => port > 0)
                    .Distinct()
                    .Select(port => new V1ServicePort
                    {
                        Name = $"tcp-{port}",
                        Protocol = "TCP",
                        Port = port,
                        TargetPort = port
                    })
                    .ToList()
            }
        };

    public static V1Ingress Ingress(
        string namespaceName,
        string name,
        string host,
        string serviceName,
        int servicePort,
        KubernetesIngressSpec spec,
        IReadOnlyDictionary<string, string>? extraLabels = null)
    {
        var path = string.IsNullOrWhiteSpace(spec.Path) ? "/" : spec.Path;
        return new V1Ingress
        {
            Metadata = new V1ObjectMeta
            {
                Name = name,
                NamespaceProperty = namespaceName,
                Labels = MergeSystemLabels(BuildWorkloadLabels(name, null, extraLabels), extraLabels)
            },
            Spec = new V1IngressSpec
            {
                IngressClassName = string.IsNullOrWhiteSpace(spec.ClassName) ? null : spec.ClassName,
                Rules =
                [
                    new V1IngressRule
                    {
                        Host = host,
                        Http = new V1HTTPIngressRuleValue
                        {
                            Paths =
                            [
                                new V1HTTPIngressPath
                                {
                                    Path = path,
                                    PathType = "Prefix",
                                    Backend = new V1IngressBackend
                                    {
                                        Service = new V1IngressServiceBackend
                                        {
                                            Name = serviceName,
                                            Port = new V1ServiceBackendPort { Number = servicePort }
                                        }
                                    }
                                }
                            ]
                        }
                    }
                ],
                Tls = string.IsNullOrWhiteSpace(spec.TlsSecretName)
                    ? null
                    :
                    [
                        new V1IngressTLS
                        {
                            Hosts = [host],
                            SecretName = spec.TlsSecretName
                        }
                    ]
            }
        };
    }

    public static int? ResolvePort(ContainerConfig config, OrchestrationSpec spec)
        => spec.ExposedPort is > 0
            ? spec.ExposedPort
            : config.PortMappings?.Keys.FirstOrDefault(port => port > 0);

    public static OrchestrationExposureType ResolveExposure(OrchestrationSpec spec, KubernetesRunnerOptions options)
    {
        if (spec.Kubernetes.Exposure != OrchestrationExposureType.None)
            return spec.Kubernetes.Exposure;
        return Enum.TryParse<OrchestrationExposureType>(options.DefaultExposure, true, out var value)
            ? value
            : OrchestrationExposureType.NodePort;
    }

    public static OrchestrationNetworkMode ResolveNetworkMode(OrchestrationSpec spec, KubernetesRunnerOptions options)
    {
        if (spec.Kubernetes.NetworkMode != OrchestrationNetworkMode.Isolated)
            return spec.Kubernetes.NetworkMode;
        return Enum.TryParse<OrchestrationNetworkMode>(options.NetworkMode, true, out var value)
            ? value
            : OrchestrationNetworkMode.Isolated;
    }

    private static V1PodSpec PodSpec(
        string name,
        ContainerConfig config,
        KubernetesRunnerOptions options,
        OrchestrationSpec spec,
        int? containerPort,
        string restartPolicy = "Always")
    {
        var k8s = spec.Kubernetes;
        var env = new Dictionary<string, string>(spec.Environment);
        foreach (var (key, value) in config.EnvironmentVariables ?? [])
            env[key] = value;

        var pullSecrets = k8s.ImagePullSecrets.Count > 0
            ? k8s.ImagePullSecrets
            : options.ImagePullSecrets.ToList();
        pullSecrets.AddRange(options.Registries.Select(KubernetesRegistrySecretFactory.SecretName));
        pullSecrets = pullSecrets.Distinct(StringComparer.Ordinal).ToList();

        return new V1PodSpec
        {
            RestartPolicy = restartPolicy,
            AutomountServiceAccountToken = k8s.Security.AutomountServiceAccountToken,
            EnableServiceLinks = false,
            DnsPolicy = options.DnsServers.Length > 0 ? "None" : "ClusterFirst",
            DnsConfig = options.DnsServers.Length > 0
                ? new V1PodDNSConfig { Nameservers = options.DnsServers.ToList() }
                : null,
            NodeSelector = k8s.NodeSelector.Count > 0 ? k8s.NodeSelector : null,
            Tolerations = k8s.Tolerations.Select(t => new V1Toleration
            {
                Key = t.Key,
                OperatorProperty = t.Operator,
                Value = t.Value,
                Effect = t.Effect
            }).ToList(),
            ImagePullSecrets = pullSecrets.Select(secret => new V1LocalObjectReference { Name = secret }).ToList(),
            Volumes = BuildVolumes(k8s.Volumes),
            Containers =
            [
                new V1Container
                {
                    Name = name,
                    Image = config.Image,
                    ImagePullPolicy = string.IsNullOrWhiteSpace(k8s.ImagePullPolicy) ? "IfNotPresent" : k8s.ImagePullPolicy,
                    Command = ResolveCommand(config),
                    Args = ResolveArgs(config),
                    Env = env.OrderBy(kvp => kvp.Key, StringComparer.Ordinal)
                        .Select(kvp => new V1EnvVar { Name = kvp.Key, Value = kvp.Value })
                        .ToList(),
                    Ports = containerPort is > 0 ? [new V1ContainerPort { ContainerPort = containerPort.Value }] : null,
                    Resources = BuildResources(options, k8s.Resources),
                    SecurityContext = BuildSecurityContext(k8s.Security),
                    VolumeMounts = k8s.Volumes
                        .Where(v => !string.IsNullOrWhiteSpace(v.Name) && !string.IsNullOrWhiteSpace(v.MountPath))
                        .Select(v => new V1VolumeMount
                        {
                            MountPath = v.MountPath,
                            Name = KubernetesNames.SafeName(v.Name),
                            ReadOnlyProperty = v.ReadOnly
                        })
                        .ToList()
                }
            ]
        };
    }

    private static V1ResourceRequirements BuildResources(KubernetesRunnerOptions options, KubernetesResourceSpec resources)
        => new()
        {
            Requests = new Dictionary<string, ResourceQuantity>
            {
                ["cpu"] = new(string.IsNullOrWhiteSpace(resources.CpuRequest) ? options.CpuRequest : resources.CpuRequest),
                ["memory"] = new(string.IsNullOrWhiteSpace(resources.MemoryRequest) ? options.MemoryRequest : resources.MemoryRequest)
            },
            Limits = new Dictionary<string, ResourceQuantity>
            {
                ["cpu"] = new(string.IsNullOrWhiteSpace(resources.CpuLimit) ? options.CpuLimit : resources.CpuLimit),
                ["memory"] = new(string.IsNullOrWhiteSpace(resources.MemoryLimit) ? options.MemoryLimit : resources.MemoryLimit),
                ["ephemeral-storage"] = new(string.IsNullOrWhiteSpace(resources.EphemeralStorageLimit) ? options.EphemeralStorageLimit : resources.EphemeralStorageLimit)
            }
        };

    private static V1SecurityContext BuildSecurityContext(KubernetesSecuritySpec security)
        => new()
        {
            AllowPrivilegeEscalation = security.AllowPrivilegeEscalation,
            RunAsNonRoot = security.RunAsNonRoot,
            RunAsUser = security.RunAsUser,
            RunAsGroup = security.RunAsGroup,
            ReadOnlyRootFilesystem = security.ReadOnlyRootFilesystem,
            Capabilities = new V1Capabilities
            {
                Drop = security.CapabilitiesDrop.Count > 0 ? security.CapabilitiesDrop : ["ALL"],
                Add = security.CapabilitiesAdd.Count > 0 ? security.CapabilitiesAdd : null
            }
        };

    private static List<V1Volume> BuildVolumes(IEnumerable<KubernetesVolumeSpec> volumes)
    {
        var result = new List<V1Volume>();
        foreach (var volume in volumes)
        {
            if (string.IsNullOrWhiteSpace(volume.Name))
                continue;

            var name = KubernetesNames.SafeName(volume.Name);
            if (volume.Type.Equals("secret", StringComparison.OrdinalIgnoreCase))
            {
                result.Add(new V1Volume { Name = name, Secret = new V1SecretVolumeSource { SecretName = name } });
            }
            else if (volume.Type.Equals("configMap", StringComparison.OrdinalIgnoreCase))
            {
                result.Add(new V1Volume { Name = name, ConfigMap = new V1ConfigMapVolumeSource { Name = name } });
            }
            else
            {
                result.Add(new V1Volume { Name = name, EmptyDir = new V1EmptyDirVolumeSource() });
            }
        }

        return result;
    }

    private static Dictionary<string, string> BuildWorkloadLabels(
        string name,
        IReadOnlyDictionary<string, string>? configLabels,
        IReadOnlyDictionary<string, string>? extraLabels)
    {
        var labels = CommonLabels("challenge-workload", name);
        foreach (var (key, value) in configLabels ?? new Dictionary<string, string>())
        {
            if (IsSafeUserLabelKey(key) && !string.IsNullOrWhiteSpace(value))
                labels[key] = KubernetesNames.SafeName(value);
        }
        foreach (var (key, value) in extraLabels ?? new Dictionary<string, string>())
        {
            if (IsSafeLabelKey(key) && !string.IsNullOrWhiteSpace(value))
                labels[key] = KubernetesNames.SafeName(value);
        }
        labels["app"] = name;
        labels[InstanceLabel] = KubernetesNames.SafeName(name);
        return labels;
    }

    private static void ApplyLabels(Dictionary<string, string> labels, IReadOnlyDictionary<string, string>? extra)
    {
        foreach (var (key, value) in extra ?? new Dictionary<string, string>())
        {
            if (IsSafeUserLabelKey(key) && !string.IsNullOrWhiteSpace(value))
                labels[key] = KubernetesNames.SafeName(value);
        }
    }

    private static List<string>? ResolveCommand(ContainerConfig config)
    {
        if (config.Entrypoint is { Count: > 0 })
            return config.Entrypoint.ToList();

        return string.IsNullOrWhiteSpace(config.Command)
            ? null
            : ["/bin/sh", "-c"];
    }

    private static List<string>? ResolveArgs(ContainerConfig config)
        => string.IsNullOrWhiteSpace(config.Command) ? null : [config.Command];

    private static Dictionary<string, string> SelectorLabels(string name)
        => new() { ["app"] = name, [InstanceLabel] = KubernetesNames.SafeName(name) };

    private static Dictionary<string, string> MergeUserLabels(
        Dictionary<string, string> baseLabels,
        IReadOnlyDictionary<string, string>? extra)
    {
        foreach (var (key, value) in extra ?? new Dictionary<string, string>())
        {
            if (IsSafeUserLabelKey(key) && !string.IsNullOrWhiteSpace(value))
                baseLabels[key] = KubernetesNames.SafeName(value);
        }
        return baseLabels;
    }

    private static Dictionary<string, string> MergeSystemLabels(
        Dictionary<string, string> baseLabels,
        IReadOnlyDictionary<string, string>? extra)
    {
        foreach (var (key, value) in extra ?? new Dictionary<string, string>())
        {
            if (IsSafeLabelKey(key) && !string.IsNullOrWhiteSpace(value))
                baseLabels[key] = KubernetesNames.SafeName(value);
        }
        return baseLabels;
    }

    private static bool IsSafeUserLabelKey(string key)
        => IsSafeLabelKey(key) && !IsReservedLabelKey(key);

    private static bool IsReservedLabelKey(string key)
        => key.Equals("app", StringComparison.OrdinalIgnoreCase) ||
           key.Equals(ManagedByLabel, StringComparison.OrdinalIgnoreCase) ||
           key.Equals("app.kubernetes.io/part-of", StringComparison.OrdinalIgnoreCase) ||
           key.Equals("app.kubernetes.io/component", StringComparison.OrdinalIgnoreCase) ||
           key.Equals(InstanceLabel, StringComparison.OrdinalIgnoreCase) ||
           key.Equals(ProjectLabel, StringComparison.OrdinalIgnoreCase) ||
           key.Equals(ServiceLabel, StringComparison.OrdinalIgnoreCase);

    private static bool IsSafeLabelKey(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
            return false;

        var parts = key.Split('/', StringSplitOptions.TrimEntries);
        if (parts.Length is < 1 or > 2 || parts.Any(string.IsNullOrWhiteSpace))
            return false;

        return IsLabelName(parts[^1]) &&
               (parts.Length == 1 || IsDnsPrefix(parts[0]));
    }

    private static bool IsLabelName(string value)
        => value.Length <= 63 &&
           char.IsLetterOrDigit(value[0]) &&
           char.IsLetterOrDigit(value[^1]) &&
           value.All(ch => char.IsLetterOrDigit(ch) || ch is '-' or '_' or '.');

    private static bool IsDnsPrefix(string value)
        => value.Length <= 253 &&
           value.Split('.', StringSplitOptions.TrimEntries)
               .All(part =>
                   part.Length is > 0 and <= 63 &&
                   char.IsLetterOrDigit(part[0]) &&
                   char.IsLetterOrDigit(part[^1]) &&
                   part.All(ch => char.IsLetterOrDigit(ch) || ch == '-'));
}
