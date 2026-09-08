using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Docker.DotNet;
using Docker.DotNet.Models;

namespace NoCTF.Runtime.Docker.PublicAccess;

/// <summary>
/// Executes bounded control operations inside one immutable dedicated client container. Docker is held only
/// by the trusted Runner, not mounted in either SSH or relay containers. The caller owns the DockerClient.
/// </summary>
public sealed partial class DockerSshControl : ISharedSshControl
{
    private readonly DockerClient docker;
    private readonly string clientId;

    public DockerSshControl(DockerClient docker, string clientId)
    {
        ArgumentNullException.ThrowIfNull(docker);
        if (clientId.Length != 64 || !clientId.All(char.IsAsciiHexDigit))
            throw new ArgumentException("Use the inspected immutable SSH client container ID, not a reusable name.", nameof(clientId));
        this.docker = docker;
        this.clientId = clientId;
    }

    public async Task<string?> ReadSessionAsync(CancellationToken ct)
    {
        var container = await docker.Containers.InspectContainerAsync(clientId, ct);
        if (container.State?.Running != true) return null;
        var check = await ExecuteAsync(["ssh", "-F", "none", "-S", "/control/master", "-O", "check", "gateway"], ct);
        if (check.Code != 0) return null;
        var match = MasterPid().Match(check.Output);
        if (!match.Success) return null;
        // PID alone can be reused; also bind to its Linux start ticks and the container start identity.
        var process = await ExecuteAsync(["cat", $"/proc/{match.Groups[1].Value}/stat"], ct);
        if (process.Code != 0) return null;
        var end = process.Output.LastIndexOf(')');
        if (end < 0) return null;
        var fields = process.Output[(end + 1)..].Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (fields.Length < 20 || !ulong.TryParse(fields[19], NumberStyles.None, CultureInfo.InvariantCulture, out var ticks)) return null;
        return $"{clientId}:{container.State.StartedAt}:{match.Groups[1].Value}:{ticks.ToString(CultureInfo.InvariantCulture)}";
    }

    public async Task<bool> ForwardAsync(SshPortLease lease, SshForwardOperation operation, CancellationToken ct)
    {
        if (lease.PublicationId == Guid.Empty || lease.PublicPort is < 1024 or > 65535 || lease.ContainerPort is < 1 or > 65535)
            throw new ArgumentException("Invalid SSH publication binding.", nameof(lease));
        var command = operation switch
        {
            SshForwardOperation.Publish => "forward",
            SshForwardOperation.Revoke => "cancel",
            _ => throw new ArgumentOutOfRangeException(nameof(operation))
        };
        // Never load client.conf here: its persisted RemoteForward entries could affect unrelated ports.
        // Include the unique target Unix path even for cancellation; never cancel by public port alone.
        var specification = FormattableString.Invariant($"0.0.0.0:{lease.PublicPort}:/endpoints/{lease.PublicationId:D}/listen/{lease.ContainerPort}.sock");
        var result = await ExecuteAsync(["ssh", "-F", "none", "-S", "/control/master", "-O", command, "-R", specification, "gateway"], ct);
        // OpenSSH 10.3's `-O cancel` prints a master rejection but still exits 0. With fixed ports
        // and `-F none`, an acknowledged operation is silent. Treat any diagnostic as uncertain;
        // never free a reservation solely because the CLI exit status was zero.
        return result.Code == 0 && string.IsNullOrWhiteSpace(result.Output);
    }

    private async Task<(long Code, string Output)> ExecuteAsync(string[] command, CancellationToken ct)
    {
        // BusyBox timeout belongs to the dedicated Alpine client image. No shell interpolation. It also
        // terminates an exec after the caller cancels its HTTP stream, avoiding orphaned control processes.
        var exec = await docker.Exec.CreateContainerExecAsync(clientId, new ContainerExecCreateParameters
        { Cmd = ["timeout", "-s", "KILL", "4", .. command], AttachStdout = true, AttachStderr = true }, ct);
        using var output = await docker.Exec.StartContainerExecAsync(exec.ID, new ContainerExecStartParameters { Detach = false, TTY = false }, ct);
        var buffer = new byte[4096];
        var text = new StringBuilder();
        while (true)
        {
            var read = await output.ReadOutputAsync(buffer, 0, buffer.Length, ct);
            if (read.Count == 0) break;
            if (text.Length + read.Count > 8192) throw new InvalidOperationException("SSH control output exceeded its bounded protocol limit.");
            text.Append(Encoding.UTF8.GetString(buffer, 0, read.Count));
        }
        return ((await docker.Exec.InspectContainerExecAsync(exec.ID, ct)).ExitCode ?? -1, text.ToString());
    }

    [GeneratedRegex(@"Master running \(pid=([1-9][0-9]{0,9})\)", RegexOptions.CultureInvariant)]
    private static partial Regex MasterPid();
}
