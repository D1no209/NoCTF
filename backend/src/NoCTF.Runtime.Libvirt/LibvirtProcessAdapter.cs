using CliWrap;
using CliWrap.Buffered;

namespace NoCTF.Runtime.Libvirt;

public sealed record LibvirtProcessResult(int ExitCode, string StandardOutput, string StandardError);

public interface ILibvirtProcessAdapter
{
    Task<LibvirtProcessResult> RunAsync(
        string executable,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken);
}

public sealed class LibvirtProcessAdapter : ILibvirtProcessAdapter
{
    private static readonly HashSet<string> AllowedExecutables =
        new(StringComparer.Ordinal)
        {
            "virsh",
            "qemu-img",
            "virt-install",
            "tar"
        };

    public async Task<LibvirtProcessResult> RunAsync(
        string executable,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken)
    {
        if (!AllowedExecutables.Contains(executable))
            throw new InvalidOperationException(
                $"Executable '{executable}' is not allowed by the Libvirt process adapter.");
        var result = await Cli.Wrap(executable)
            .WithArguments(arguments)
            .WithValidation(CommandResultValidation.None)
            .ExecuteBufferedAsync(cancellationToken);
        return new LibvirtProcessResult(
            result.ExitCode,
            result.StandardOutput,
            result.StandardError);
    }
}
