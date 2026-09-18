using NoCTF.Application.Runtime.Instances;
using NoCTF.Application.Runtime.Provisioning;
using NoCTF.Domain.Runtime;

namespace NoCTF.Application.Runtime.Capacity;

/// <summary>Checks the actual provider request against its immutable active allocation.</summary>
public static class RuntimeProvisionCapacity
{
    public static bool Matches(RuntimeCapacityAllocation allocation, IRuntimeProvisionMessage message)
    {
        if (allocation.Identity.RuntimeInstanceId != message.RuntimeInstanceId || allocation.RunnerId != message.RunnerId)
            return false;
        var policy = new RuntimeResourceBudgetPolicy();
        RuntimeResourceLimits limits;
        RuntimeResourceLimits budget;
        switch (message)
        {
            case ProvisionContainerRuntime container when container.Definition.OperationId == allocation.Identity.OperationId:
                limits = policy.EffectiveLimit(container.Definition.Limits, container.Definition.Provider);
                budget = policy.EffectiveLimit(container.Definition.Budget ?? limits, container.Definition.Provider);
                break;
            case ProvisionComposeRuntime compose when compose.Definition.OperationId == allocation.Identity.OperationId:
                var services = compose.Definition.ServiceResources;
                var budgets = compose.Definition.ServiceBudgets ?? services;
                if (services.Count == 0 || services.Count != budgets.Count) return false;
                foreach (var (name, limit) in services)
                {
                    var effective = policy.EffectiveLimit(limit, compose.Definition.Provider);
                    if (!budgets.TryGetValue(name, out var requested) || requested.MemoryBytes != effective.MemoryBytes
                        || requested.PidsLimit != effective.PidsLimit || requested.NanoCpus <= 0 || requested.NanoCpus > effective.NanoCpus)
                        return false;
                }
                limits = RuntimeResourceBudgetPolicy.Sum(services.Values.Select(value => policy.EffectiveLimit(value, compose.Definition.Provider)),
                    compose.Definition.Limits.PidsLimit);
                budget = RuntimeResourceBudgetPolicy.Sum(budgets.Values.Select(value => policy.EffectiveLimit(value, compose.Definition.Provider)),
                    compose.Definition.Limits.PidsLimit);
                break;
            case ProvisionOvaRuntime ova when ova.Definition.OperationId == allocation.Identity.OperationId:
                limits = budget = ova.Definition.Limits;
                break;
            default: return false;
        }
        return RuntimeResourceBudgetPolicy.ToAmount(limits) == allocation.Limit
            && RuntimeResourceBudgetPolicy.ToAmount(budget) == allocation.Budget;
    }
}
