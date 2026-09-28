using NoCTF.Application.Runtime.Provisioning;
using NoCTF.Domain.Runtime;

namespace NoCTF.Application.Runtime.Capacity;

public sealed class RuntimeResourceBudgetPolicy
{
    public RuntimeResourceLimits EffectiveLimit(RuntimeResourceLimits limits, RuntimeProvider provider)
    {
        if (limits.MemoryBytes <= 0 || limits.NanoCpus <= 0 || limits.PidsLimit < 0)
            throw new InvalidOperationException("Workload resource limits must be positive.");
        return provider == RuntimeProvider.Kubernetes
            ? limits with { NanoCpus = RoundUp(limits.NanoCpus, 1_000_000) }
            : limits;
    }

    public RuntimeResourceLimits Calculate(RuntimeResourceLimits limits, RuntimeProvider provider, bool auxiliary = false)
        => EffectiveLimit(limits, provider);

    public static RuntimeResourceLimits Sum(IEnumerable<RuntimeResourceLimits> resources, long inheritedPids = 0)
    {
        var values = resources.ToArray();
        var pids = values.Sum(value => checked(value.PidsLimit));
        return new(values.Sum(value => checked(value.MemoryBytes)), values.Sum(value => checked(value.NanoCpus)),
            pids > 0 ? pids : inheritedPids);
    }

    public IReadOnlyDictionary<string, RuntimeResourceLimits> ForCompose(
        IReadOnlyDictionary<string, RuntimeResourceLimits> services, RuntimeProvider provider) =>
        services.ToDictionary(pair => pair.Key, pair => EffectiveLimit(pair.Value, provider), StringComparer.Ordinal);

    public static IReadOnlyDictionary<string, RuntimeResourceLimits> RecreateComposeBudgets(
        IReadOnlyDictionary<string, RuntimeResourceLimits> services, RuntimeProvider provider, RuntimeResourceAmount committed)
    {
        var budgets = new RuntimeResourceBudgetPolicy().ForCompose(services, provider);
        var total = Sum(budgets.Values, committed.PidsLimit);
        if (total.MemoryBytes == committed.MemoryBytes
            && total.NanoCpus == committed.NanoCpus
            && total.PidsLimit == committed.PidsLimit)
            return budgets;
        throw new InvalidOperationException("Compose requests cannot be reconstructed from the committed allocation.");
    }

    public static RuntimeResourceLimits ToLimits(RuntimeResourceAmount amount) => new(amount.MemoryBytes, amount.NanoCpus, amount.PidsLimit);
    public static RuntimeResourceAmount ToAmount(RuntimeResourceLimits limits) => new(limits.MemoryBytes, limits.NanoCpus, limits.PidsLimit);
    private static long RoundUp(long value, long unit) => checked((value / unit + (value % unit == 0 ? 0 : 1)) * unit);
}
