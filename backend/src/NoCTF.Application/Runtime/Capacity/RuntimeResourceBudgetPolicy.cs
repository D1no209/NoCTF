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

    public static IReadOnlyDictionary<string, RuntimeResourceLimits> RestoreComposeBudgets(
        IReadOnlyDictionary<string, RuntimeResourceLimits> services, RuntimeProvider provider, RuntimeResourceAmount committed)
    {
        foreach (var factor in new[] { 1, 2 })
        {
            var budgets = services.ToDictionary(pair => pair.Key, pair => LegacyBudget(pair.Value, provider, factor),
                StringComparer.Ordinal);
            var sum = Sum(budgets.Values, committed.PidsLimit);
            if (sum.MemoryBytes == committed.MemoryBytes && sum.NanoCpus == committed.NanoCpus
                && sum.PidsLimit == committed.PidsLimit)
                return budgets;
        }
        // Legacy strict claims may reserve more than the service aggregate. Keep
        // those conservative claims while applying unchanged full service requests.
        var strict = new RuntimeResourceBudgetPolicy().ForCompose(services, provider);
        var full = Sum(strict.Values, committed.PidsLimit);
        if (full.MemoryBytes <= committed.MemoryBytes && full.NanoCpus <= committed.NanoCpus && full.PidsLimit <= committed.PidsLimit)
            return strict;
        throw new InvalidOperationException("Compose requests cannot be reconstructed from the committed allocation.");
    }

    public static RuntimeResourceLimits ToLimits(RuntimeResourceAmount amount) => new(amount.MemoryBytes, amount.NanoCpus, amount.PidsLimit);
    public static RuntimeResourceAmount ToAmount(RuntimeResourceLimits limits) => new(limits.MemoryBytes, limits.NanoCpus, limits.PidsLimit);
    private static RuntimeResourceLimits LegacyBudget(RuntimeResourceLimits limits, RuntimeProvider provider, int factor)
    {
        var effective = new RuntimeResourceBudgetPolicy().EffectiveLimit(limits, provider);
        if (provider == RuntimeProvider.Libvirt || factor == 1) return effective;
        var cpu = effective.NanoCpus / factor + (effective.NanoCpus % factor == 0 ? 0 : 1);
        return effective with { NanoCpus = provider == RuntimeProvider.Kubernetes ? RoundUp(cpu, 1_000_000) : cpu };
    }
    private static long RoundUp(long value, long unit) => checked((value / unit + (value % unit == 0 ? 0 : 1)) * unit);
}
