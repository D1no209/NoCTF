using System.Diagnostics;

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
        var startInfo = new ProcessStartInfo(executable)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException($"Unable to start {executable}.");
        var standardOutput = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var standardError = process.StandardError.ReadToEndAsync(cancellationToken);
        try
        {
            await process.WaitForExitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
            throw;
        }
        return new LibvirtProcessResult(
            process.ExitCode,
            await standardOutput,
            await standardError);
    }
}
