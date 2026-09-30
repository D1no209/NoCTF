using NoCTF.Application.Runtime.Capacity;
using NoCTF.Application.Runtime.Provisioning;
using NoCTF.Runtime.Kubernetes.Configuration;

namespace NoCTF.Tests.Unit.Runtime;

public sealed class RuntimeResourceBudgetPolicyTests
{
    [Test]
    public async Task Service_resources_use_millicores_and_sum_platform_process_limits()
    {
        var definition = new ContainerRuntimeDefinition([new("web", "web", .501m, 128), new("db", "db", .501m, 256)]);
        var policy = new RuntimeResourceBudgetPolicy(new() { ProcessesPerService = 192 });
        var resources = policy.ForServices(definition);
        await Assert.That(resources.CpuMillicores).IsEqualTo(1002);
        await Assert.That(resources.MemoryBytes).IsEqualTo(384L * 1024 * 1024);
        await Assert.That(resources.PidsLimit).IsEqualTo(384);
        var workload = KubernetesWorkloadResources.Create(definition.Services[0].Resources(192));
        await Assert.That(workload.Limits["cpu"].ToDecimal()).IsEqualTo(.501m);
        await Assert.That(workload.Requests["memory"].ToDecimal()).IsEqualTo(128m * 1024 * 1024);
    }
}
