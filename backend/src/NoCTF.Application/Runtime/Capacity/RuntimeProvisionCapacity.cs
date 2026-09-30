using NoCTF.Application.Runtime.Instances;
using NoCTF.Application.Runtime.Provisioning;
using NoCTF.Domain.Runtime;

namespace NoCTF.Application.Runtime.Capacity;

public static class RuntimeProvisionCapacity
{
    public static bool Matches(RuntimeCapacityAllocation allocation, IRuntimeProvisionMessage message)
    {
        if (allocation.Identity.RuntimeInstanceId != message.RuntimeInstanceId || allocation.RunnerId != message.RunnerId) return false;
        var resources = message switch
        {
            ProvisionContainerRuntime container when container.Definition.OperationId == allocation.Identity.OperationId
                => RuntimeResourceBudgetPolicy.Sum(container.Definition.ServiceResources.Values),
            ProvisionOvaRuntime ova when ova.Definition.OperationId == allocation.Identity.OperationId => ova.Definition.Limits,
            _ => null
        };
        if (message is ProvisionContainerRuntime containerMessage && resources != containerMessage.Definition.Limits) return false;
        return resources is not null && RuntimeResourceBudgetPolicy.ToAmount(resources) == allocation.Limit
            && RuntimeResourceBudgetPolicy.ToAmount(resources) == allocation.Budget;
    }
}
