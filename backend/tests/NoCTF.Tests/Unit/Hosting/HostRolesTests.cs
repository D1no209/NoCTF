using Microsoft.Extensions.Configuration;
using NoCTF.Hosting;

namespace NoCTF.Tests.Unit.Hosting;

public sealed class HostRolesTests
{
    [Test]
    public async Task Missing_configuration_defaults_to_all_roles()
    {
        var roles = HostRoles.FromConfiguration(BuildConfiguration([]));

        await Assert.That(roles.Values)
            .IsEquivalentTo([HostRole.Api, HostRole.Worker, HostRole.Runner]);
    }

    [Test]
    [Arguments("Api")]
    [Arguments("Worker")]
    [Arguments("Runner")]
    [Arguments("Api,Worker")]
    [Arguments("Api,Runner")]
    [Arguments("Worker,Runner")]
    [Arguments("Api,Worker,Runner")]
    public async Task Every_non_empty_role_combination_is_supported(string configuredRoles)
    {
        var values = configuredRoles.Split(',');
        var entries = values
            .Select((value, index) => new KeyValuePair<string, string?>(
                $"Hosting:Roles:{index}",
                value));

        var roles = HostRoles.FromConfiguration(BuildConfiguration(entries));

        await Assert.That(roles.Values.Select(role => role.ToString()))
            .IsEquivalentTo(values);
    }

    [Test]
    public async Task Duplicate_roles_are_normalized()
    {
        var roles = HostRoles.FromConfiguration(BuildConfiguration(
        [
            new("Hosting:Roles:0", "Worker"),
            new("Hosting:Roles:1", "worker"),
            new("Hosting:Roles:2", "Api")
        ]));

        await Assert.That(roles.Values).IsEquivalentTo([HostRole.Api, HostRole.Worker]);
    }

    [Test]
    [Arguments("")]
    [Arguments("Database")]
    public async Task Empty_or_unknown_roles_fail_fast(string value)
    {
        var configuration = BuildConfiguration([new("Hosting:Roles", value)]);

        var exception = Assert.Throws<InvalidOperationException>(() =>
            HostRoles.FromConfiguration(configuration));

        await Assert.That(exception.Message).Contains("Hosting:Roles");
    }

    private static IConfiguration BuildConfiguration(
        IEnumerable<KeyValuePair<string, string?>> values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values).Build();
}
