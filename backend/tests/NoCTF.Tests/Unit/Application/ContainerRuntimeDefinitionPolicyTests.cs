using NoCTF.Application.Runtime.Configuration;
using NoCTF.Application.Runtime.Provisioning;

namespace NoCTF.Tests.Unit.Application;

public sealed class ContainerRuntimeDefinitionPolicyTests
{
    [Test]
    public async Task Named_services_can_reuse_ports_without_a_second_public_port_list()
    {
        var definition = new ContainerRuntimeDefinition([new("web", "web:latest"), new("db", "db:latest")]);
        RuntimeUrlBinding[] entries = [new("http://{HOST}:{PORT}", RuntimeExposure.OwnerOnly, 8080, "web"), new("nc {HOST} {PORT}", RuntimeExposure.OwnerOnly, 8080, "db")];
        await Assert.That(ContainerRuntimeDefinitionPolicy.Validate(definition, entries)).IsEmpty();
    }

    [Test]
    [Arguments("")]
    [Arguments("Web")]
    [Arguments("-web")]
    [Arguments("web-")]
    [Arguments("web_db")]
    public async Task Invalid_service_names_are_rejected(string name) =>
        await Assert.That(ContainerRuntimeDefinitionPolicy.Validate(new([new(name, "alpine")]))).IsNotEmpty();

    [Test]
    public async Task Duplicate_and_missing_services_are_rejected()
    {
        await Assert.That(ContainerRuntimeDefinitionPolicy.Validate(new([new("web", "alpine"), new("web", "alpine")]))).IsNotEmpty();
        await Assert.That(ContainerRuntimeDefinitionPolicy.Validate(new([]))).IsNotEmpty();
        await Assert.That(ContainerRuntimeDefinitionPolicy.Validate(new(Enumerable.Range(0, 65).Select(index => new RuntimeServiceDefinition($"service-{index}", "alpine")).ToArray()))).IsNotEmpty();
    }

    [Test]
    public async Task All_operations_require_an_existing_service()
    {
        var definition = new ContainerRuntimeDefinition([new("main", "alpine")]);
        var entry = new RuntimeUrlBinding("http://{HOST}:{PORT}", RuntimeExposure.OwnerOnly, 80, "missing");
        await Assert.That(ContainerRuntimeDefinitionPolicy.Validate(definition, [entry])).IsNotEmpty();
        await Assert.That(ContainerRuntimeDefinitionPolicy.Validate(definition, control: entry)).IsNotEmpty();
        await Assert.That(ContainerRuntimeDefinitionPolicy.Validate(definition, checker: new("missing"))).IsNotEmpty();
    }

    [Test]
    [Arguments(0.0005)]
    [Arguments(0)]
    [Arguments(-1)]
    public async Task Cpu_is_positive_and_uses_exact_millicores(double cores) =>
        await Assert.That(ContainerRuntimeDefinitionPolicy.Validate(new([new("main", "alpine", (decimal)cores)]))).IsNotEmpty();

    [Test]
    public async Task Provider_resource_names_are_deterministic_and_fit_kubernetes_labels()
    {
        var id = Guid.NewGuid();
        var name = new string('a', 63);
        var resource = NamedContainerRuntime.ResourceName(id, name);
        await Assert.That(resource.Length).IsLessThanOrEqualTo(63);
        await Assert.That(resource).IsEqualTo(NamedContainerRuntime.ResourceName(id, name));
        await Assert.That(resource).IsNotEqualTo(NamedContainerRuntime.ResourceName(id, new string('a', 62) + "b"));
    }
}
