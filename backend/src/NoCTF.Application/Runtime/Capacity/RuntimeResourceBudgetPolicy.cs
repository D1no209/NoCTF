using NoCTF.Application.Runtime.Provisioning;
using NoCTF.Domain.Runtime;

namespace NoCTF.Application.Runtime.Capacity;

public sealed class RuntimeResourceBudgetPolicy(RuntimeExecutionOptions? options = null)
{
    public long ProcessesPerService => options?.ProcessesPerService ?? 256;
    public RuntimeResourceLimits EffectiveLimit(RuntimeResourceLimits limits, RuntimeProvider provider)
    {
        if (limits.MemoryBytes <= 0 || limits.CpuMillicores <= 0 || limits.PidsLimit <= 0)
            throw new InvalidOperationException("Workload resource limits must be positive.");
        return limits;
    }
    public RuntimeResourceLimits Calculate(RuntimeResourceLimits limits, RuntimeProvider provider, bool auxiliary = false) => EffectiveLimit(limits, provider);
    public RuntimeResourceLimits ForServices(ContainerRuntimeDefinition definition) => Sum(definition.Services.Select(service => service.Resources(ProcessesPerService)));
    public static RuntimeResourceLimits Sum(IEnumerable<RuntimeResourceLimits> resources, long inheritedPids = 0)
    {
        var values = resources.ToArray();
        var pids = values.Sum(value => checked(value.PidsLimit));
        return new(values.Sum(value => checked(value.MemoryBytes)), values.Sum(value => checked(value.CpuMillicores)), pids > 0 ? pids : inheritedPids);
    }
    public static RuntimeResourceLimits ToLimits(RuntimeResourceAmount amount) => new(amount.MemoryBytes, amount.CpuMillicores, amount.PidsLimit);
    public static RuntimeResourceAmount ToAmount(RuntimeResourceLimits limits) => new(limits.MemoryBytes, limits.CpuMillicores, limits.PidsLimit);
}
