using NoCTF.Domain.Platform;

namespace NoCTF.Runtime.Docker.PublicAccess;

/// <summary>One acknowledged SSH forwarding identity. Never reconstruct this handle using only a port number.</summary>
public sealed record SshPortLease(Guid PublicationId, int ContainerPort, int PublicPort, string SessionId);

/// <summary>Only the OpenSSH adapter translates these operations to command-line arguments.</summary>
public enum SshForwardOperation { Publish, Revoke }
public enum SshRevocationResult { Applied, AlreadyAbsent, Uncertain }

/// <summary>Controls a dedicated, already authenticated SSH master; it does not carry player traffic.</summary>
public interface ISharedSshControl
{
    Task<string?> ReadSessionAsync(CancellationToken ct);
    Task<bool> ForwardAsync(SshPortLease lease, SshForwardOperation operation, CancellationToken ct);
    async Task<SshRevocationResult> RevokeAsync(SshPortLease lease, CancellationToken ct) =>
        await ForwardAsync(lease, SshForwardOperation.Revoke, ct) ? SshRevocationResult.Applied : SshRevocationResult.Uncertain;
}

/// <summary>A stable, safe-to-log classification without raw SSH output or credentials.</summary>
public sealed class PublicTunnelException(PublicAccessFailure failure) : Exception($"Public tunnel operation failed: {failure}.")
{
    public PublicAccessFailure Failure { get; } = failure;
}

/// <summary>
/// Serializes bounded control commands, not network traffic. Uncertain commands retain their port reservation
/// until cancellation is acknowledged or the old master is gone. Not a cross-process ownership mechanism:
/// its caller must hold the deployment connector lease before performing any operation.
/// </summary>
public sealed class SharedSshPortController : IDisposable
{
    private readonly ISharedSshControl control;
    private readonly int[] ports;
    private readonly Dictionary<int, SshPortLease> reservations = [];
    private readonly SemaphoreSlim gate = new(1, 1);

    public SharedSshPortController(ISharedSshControl control, IReadOnlyList<int> approvedPorts)
    {
        ArgumentNullException.ThrowIfNull(control);
        ArgumentNullException.ThrowIfNull(approvedPorts);
        if (approvedPorts.Count is < 1 or > 256 || approvedPorts.Any(port => port is < 1024 or > 65535)
            || approvedPorts.Distinct().Count() != approvedPorts.Count)
            throw new ArgumentException("A bounded, unique approved TCP port pool is required.", nameof(approvedPorts));
        this.control = control;
        ports = approvedPorts.Order().ToArray();
    }

    /// <summary>Publishes only a caller-approved, live relay. Retrying the same publication keeps its port.</summary>
    public async Task<SshPortLease> PublishAsync(Guid publicationId, int containerPort, CancellationToken ct)
    {
        if (publicationId == Guid.Empty || containerPort is < 1 or > 65535)
            throw new ArgumentException("A publication identity and valid container port are required.");
        await gate.WaitAsync(ct);
        try
        {
            var session = await control.ReadSessionAsync(ct)
                ?? throw new PublicTunnelException(PublicAccessFailure.ConnectorOffline);
            var existing = reservations.Values.SingleOrDefault(item => item.PublicationId == publicationId && item.ContainerPort == containerPort);
            var port = existing?.PublicPort ?? ports.FirstOrDefault(candidate => !reservations.ContainsKey(candidate));
            if (port == 0) throw new PublicTunnelException(PublicAccessFailure.GatewayCapacityExceeded);
            var lease = new SshPortLease(publicationId, containerPort, port, session);
            // Reserve BEFORE the command: timeout/cancellation can happen after the remote side applied it.
            reservations[port] = lease;
            if (!await control.ForwardAsync(lease, SshForwardOperation.Publish, ct))
                throw new PublicTunnelException(PublicAccessFailure.PublicPortUnavailable);
            if (await control.ReadSessionAsync(ct) != session)
                throw new PublicTunnelException(PublicAccessFailure.ConnectorOffline);
            return lease;
        }
        finally { gate.Release(); }
    }

    /// <summary>
    /// Revokes an exact publication/session handle. A late revoke for A cannot cancel B on the same port,
    /// nor a newly authorized binding of A after reconnect. False means the handle was already superseded.
    /// </summary>
    public async Task<bool> RevokeAsync(SshPortLease lease, CancellationToken ct)
    {
        await gate.WaitAsync(ct);
        try
        {
            if (!reservations.TryGetValue(lease.PublicPort, out var current) || current != lease) return false;
            var session = await control.ReadSessionAsync(ct);
            // No master is not proof it has exited: a stalled process could still hold old listeners.
            if (session is null) throw new PublicTunnelException(PublicAccessFailure.ConnectorOffline);
            if (session == lease.SessionId && await control.RevokeAsync(lease, ct) == SshRevocationResult.Uncertain)
                throw new PublicTunnelException(PublicAccessFailure.PublicPortUnavailable);
            reservations.Remove(lease.PublicPort);
            return true;
        }
        finally { gate.Release(); }
    }

    /// <summary>
    /// Cleans a failed/uncertain publish that never returned a handle. The caller must first stop renewing
    /// and terminate that relay, and must serialize this with any new approval for the same publication.
    /// </summary>
    public async Task RevokePublicationAsync(Guid publicationId, CancellationToken ct)
    {
        await gate.WaitAsync(ct);
        try
        {
            var owned = reservations.Values.Where(item => item.PublicationId == publicationId).ToArray();
            if (owned.Length == 0) return;
            var session = await control.ReadSessionAsync(ct)
                ?? throw new PublicTunnelException(PublicAccessFailure.ConnectorOffline);
            foreach (var lease in owned)
            {
                if (session == lease.SessionId && await control.RevokeAsync(lease, ct) == SshRevocationResult.Uncertain)
                    throw new PublicTunnelException(PublicAccessFailure.PublicPortUnavailable);
                reservations.Remove(lease.PublicPort);
            }
        }
        finally { gate.Release(); }
    }

    public void Dispose() => gate.Dispose();
}
