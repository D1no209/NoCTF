using Microsoft.Extensions.Configuration;
using NoCTF.Application.Administration.PlatformLogs;
using NoCTF.Hosting;
using NoCTF.Hosting.Observability;

namespace NoCTF.Tests.Unit.Hosting;

public sealed class ObservabilityRoleClassificationTests
{
    [Test]
    [Arguments(HostRole.Api, PlatformLogService.Api)]
    [Arguments(HostRole.Worker, PlatformLogService.Worker)]
    [Arguments(HostRole.Runner, PlatformLogService.Runner)]
    public async Task A_single_role_uses_its_own_log_service(HostRole role, PlatformLogService expected) =>
        await Assert.That(ObservabilityExtensions.ResolveDefaultService(HostRoles.Only(role))).IsEqualTo(expected);

    [Test]
    [Arguments(HostRole.Api, HostRole.Worker)]
    [Arguments(HostRole.Api, HostRole.Runner)]
    [Arguments(HostRole.Worker, HostRole.Runner)]
    public async Task Combined_roles_use_the_host_default(HostRole first, HostRole second)
    {
        var roles = HostRoles.FromConfiguration(new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?> { ["Hosting:Roles:0"] = first.ToString(), ["Hosting:Roles:1"] = second.ToString() }).Build());
        await Assert.That(ObservabilityExtensions.ResolveDefaultService(roles)).IsEqualTo(PlatformLogService.Host);
    }

    [Test]
    public async Task All_roles_and_unspecified_roles_use_the_host_default()
    {
        await Assert.That(ObservabilityExtensions.ResolveDefaultService(HostRoles.All())).IsEqualTo(PlatformLogService.Host);
        await Assert.That(ObservabilityExtensions.ResolveDefaultService(null)).IsEqualTo(PlatformLogService.Host);
    }
}
