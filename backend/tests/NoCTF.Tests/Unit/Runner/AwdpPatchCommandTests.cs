using NoCTF.Runner.Messages;

namespace NoCTF.Tests.Unit.Runner;

public sealed class AwdpPatchCommandTests
{
    [Test]
    public async Task Only_standalone_entrypoint_argument_is_replaced()
    {
        var command = AwdpPatchCommand.Create(
            ["/bin/sh", "{entrypoint}", "prefix-{entrypoint}"],
            "scripts/fix.sh");

        await Assert.That(command).IsEquivalentTo(
            ["/bin/sh", "/noctf/fix/scripts/fix.sh", "prefix-{entrypoint}"]);
    }

    [Test]
    public async Task Default_command_executes_configured_entrypoint_without_shell_concatenation()
    {
        var command = AwdpPatchCommand.Create(null, "fix.sh");

        await Assert.That(command).IsEquivalentTo(["/bin/sh", "/noctf/fix/fix.sh"]);
    }
}
