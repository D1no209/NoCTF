using NoCTF.Application.Challenges.Capabilities;
using NoCTF.Domain.Challenges.Capabilities;

namespace NoCTF.Tests.Unit.Runner;

public sealed class FixCommandTests
{
    [Test]
    public async Task Only_the_standalone_entrypoint_argument_is_replaced()
    {
        var definition = new FixCapabilityDefinition { Entrypoint = "scripts/fix.sh", Command =
            [new() { Position = 0, Value = "/bin/sh" }, new() { Position = 1, Value = "{entrypoint}" }, new() { Position = 2, Value = "prefix-{entrypoint}" }] };
        await Assert.That(FixCommand.Create(definition)).IsEquivalentTo(new[] { "/bin/sh", "/noctf/fix/scripts/fix.sh", "prefix-{entrypoint}" });
    }

    [Test]
    public async Task The_default_command_executes_the_configured_entrypoint_as_an_argument()
    {
        await Assert.That(FixCommand.Create(new() { Entrypoint = "fix.sh" })).IsEquivalentTo(new[] { "/bin/sh", "/noctf/fix/fix.sh" });
    }

    [Test]
    public async Task A_custom_command_requires_one_standalone_entrypoint()
    {
        var definition = new FixCapabilityDefinition { Entrypoint = "fix.sh", Command =
            [new() { Position = 0, Value = "/bin/sh" }, new() { Position = 1, Value = "{entrypoint" }] };
        await Assert.That(() => FixCommand.Create(definition)).Throws<InvalidOperationException>();
    }
}
