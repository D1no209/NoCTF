using NoCTF.Application.Runtime.Capacity;
using NoCTF.Application.Runtime.Provisioning;
using NoCTF.Domain.Runtime;
using NoCTF.Runtime.Kubernetes.Configuration;

namespace NoCTF.Tests.Unit.Runtime;

public sealed class RuntimeResourceBudgetPolicyTests
{
    [Test, Arguments(1), Arguments(2)]
    public async Task Cpu_sharing_preserves_hard_limits_memory_and_pids(int factor)
    {
        var limits = new RuntimeResourceLimits(256 * 1024 * 1024, 500_000_001, 128);
        var policy = new RuntimeResourceBudgetPolicy(factor);
        var budget = policy.Calculate(limits, RuntimeProvider.Docker);
        await Assert.That(budget.NanoCpus).IsEqualTo(factor == 1 ? 500_000_001 : 250_000_001);
        await Assert.That(budget.MemoryBytes).IsEqualTo(limits.MemoryBytes);
        await Assert.That(budget.PidsLimit).IsEqualTo(limits.PidsLimit);
        await Assert.That(policy.Calculate(limits, RuntimeProvider.Libvirt)).IsEqualTo(limits);
        await Assert.That(policy.Calculate(limits, RuntimeProvider.Docker, auxiliary: true)).IsEqualTo(limits);
    }

    [Test]
    public async Task Kubernetes_rounding_matches_requests_and_compose_aggregate()
    {
        var policy = new RuntimeResourceBudgetPolicy(2);
        var services = new Dictionary<string, RuntimeResourceLimits>
        {
            ["web"] = new(128 * 1024 * 1024, 501_000_000, 64),
            ["db"] = new(256 * 1024 * 1024, 501_000_000, 128)
        };
        var budgets = policy.ForCompose(services, RuntimeProvider.Kubernetes);
        var aggregate = RuntimeResourceBudgetPolicy.Sum(budgets.Values);
        await Assert.That(aggregate.NanoCpus).IsEqualTo(502_000_000);
        await Assert.That(aggregate.MemoryBytes).IsEqualTo(384 * 1024 * 1024);
        var resources = KubernetesWorkloadResources.Create(services["web"], budgets["web"]);
        await Assert.That(resources.Limits["cpu"].ToDecimal()).IsEqualTo(.501m);
        await Assert.That(resources.Requests["cpu"].ToDecimal()).IsEqualTo(.251m);
        await Assert.That(resources.Requests["memory"].ToDecimal()).IsEqualTo(resources.Limits["memory"].ToDecimal());
        var restored = RuntimeResourceBudgetPolicy.RestoreComposeBudgets(services, RuntimeProvider.Kubernetes,
            RuntimeResourceBudgetPolicy.ToAmount(aggregate));
        await Assert.That(restored["web"]).IsEqualTo(budgets["web"]);
        await Assert.That(restored["db"]).IsEqualTo(budgets["db"]);
    }
}
