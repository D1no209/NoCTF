using System.Net;
using System.Net.Sockets;
using k8s.Models;
using NoCTF.Application.Runtime.Ports;
using NoCTF.Runtime.Kubernetes.Configuration;

namespace NoCTF.Runtime.Kubernetes.Networking;

public static class KubernetesEgressPolicy
{
    private static readonly string[] BuiltInProtectedCidrs =
    [
        "0.0.0.0/8",
        "10.0.0.0/8",
        "100.64.0.0/10",
        "127.0.0.0/8",
        "169.254.0.0/16",
        "172.16.0.0/12",
        "192.0.0.0/24",
        "192.0.2.0/24",
        "192.88.99.0/24",
        "192.168.0.0/16",
        "198.18.0.0/15",
        "198.51.100.0/24",
        "203.0.113.0/24",
        "224.0.0.0/4",
        "240.0.0.0/4"
    ];

    public static IReadOnlyList<V1NetworkPolicyEgressRule> Build(
        RuntimeEgressPolicy policy,
        V1LabelSelector runtimeSelector,
        KubernetesRuntimeOptions options)
    {
        if (!Enum.IsDefined(policy))
            throw new ArgumentOutOfRangeException(nameof(policy));

        var rules = new List<V1NetworkPolicyEgressRule>
        {
            new()
            {
                To =
                [
                    new V1NetworkPolicyPeer
                    {
                        PodSelector = runtimeSelector
                    }
                ]
            },
            DnsRule()
        };
        if (policy == RuntimeEgressPolicy.InternetOnly)
        {
            var configured = ValidateAndNormalizeProtectedCidrs(options.ProtectedCidrs);
            var except = BuiltInProtectedCidrs
                .Concat(configured)
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal)
                .ToList();
            rules.Add(new V1NetworkPolicyEgressRule
            {
                To =
                [
                    new V1NetworkPolicyPeer
                    {
                        IpBlock = new V1IPBlock
                        {
                            Cidr = "0.0.0.0/0",
                            Except = except
                        }
                    }
                ]
            });
        }
        return rules;
    }

    public static IReadOnlyList<string> ValidateAndNormalizeProtectedCidrs(
        IEnumerable<string>? cidrs)
    {
        var values = (cidrs ?? [])
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
        if (values.Length == 0)
        {
            throw new InvalidOperationException(
                "Runtime:Kubernetes:ProtectedCidrs must contain at least one IPv4 CIDR.");
        }
        foreach (var value in values)
        {
            if (!IPNetwork.TryParse(value, out var network)
                || network.BaseAddress.AddressFamily != AddressFamily.InterNetwork)
            {
                throw new InvalidOperationException(
                    $"Runtime:Kubernetes:ProtectedCidrs contains invalid IPv4 CIDR '{value}'.");
            }
        }
        return values;
    }

    private static V1NetworkPolicyEgressRule DnsRule() =>
        new()
        {
            To =
            [
                new V1NetworkPolicyPeer
                {
                    NamespaceSelector = new V1LabelSelector
                    {
                        MatchLabels = new Dictionary<string, string>
                        {
                            ["kubernetes.io/metadata.name"] = "kube-system"
                        }
                    },
                    PodSelector = new V1LabelSelector
                    {
                        MatchLabels = new Dictionary<string, string>
                        {
                            ["k8s-app"] = "kube-dns"
                        }
                    }
                }
            ],
            Ports =
            [
                new V1NetworkPolicyPort { Protocol = "UDP", Port = 53 },
                new V1NetworkPolicyPort { Protocol = "TCP", Port = 53 }
            ]
        };
}
