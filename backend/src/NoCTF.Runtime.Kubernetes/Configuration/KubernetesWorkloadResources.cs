using System.Globalization;
using k8s.Models;
using NoCTF.Application.Runtime.Capacity;
using NoCTF.Application.Runtime.Provisioning;
using NoCTF.Domain.Runtime;

namespace NoCTF.Runtime.Kubernetes.Configuration;

public static class KubernetesWorkloadResources
{
    public static V1ResourceRequirements Create(RuntimeResourceLimits limits, RuntimeResourceLimits? budget = null)
    {
        var policy = new RuntimeResourceBudgetPolicy();
        limits = policy.EffectiveLimit(limits, RuntimeProvider.Kubernetes);
        budget = policy.EffectiveLimit(budget ?? limits, RuntimeProvider.Kubernetes);
        if (budget.MemoryBytes != limits.MemoryBytes || budget.CpuMillicores > limits.CpuMillicores)
            throw new InvalidOperationException("Kubernetes requests must preserve memory and not exceed CPU limits.");
        return new V1ResourceRequirements
        {
            Limits = Values(limits),
            Requests = Values(budget)
        };
    }

    private static Dictionary<string, ResourceQuantity> Values(RuntimeResourceLimits resource) => new()
    {
        ["memory"] = new(resource.MemoryBytes.ToString(CultureInfo.InvariantCulture)),
        ["cpu"] = new($"{resource.CpuMillicores}m")
    };
}
