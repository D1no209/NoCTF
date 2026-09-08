using NoCTF.Domain.Platform;
using NoCTF.Runtime.Docker.PublicAccess;

namespace NoCTF.Tests.Unit.Runtime;

public sealed class SharedSshPortControllerTests
{
    [Test]
    public async Task Concurrent_publications_cannot_exceed_the_approved_pool()
    {
        var transport = new Control();
        using var controller = new SharedSshPortController(transport, [40000, 40002]);
        var results = await Task.WhenAll(Enumerable.Range(0, 12).Select(async _ =>
        {
            try { return await controller.PublishAsync(Guid.NewGuid(), 8080, default); }
            catch (PublicTunnelException failure)
            {
                await Assert.That(failure.Failure).IsEqualTo(PublicAccessFailure.GatewayCapacityExceeded);
                return null;
            }
        }));
        await Assert.That(results.OfType<SshPortLease>().Select(item => item.PublicPort)).IsEquivalentTo([40000, 40002]);
        await Assert.That(transport.MaximumConcurrentCommands).IsEqualTo(1);
    }

    [Test]
    public async Task Late_old_cancel_cannot_remove_a_new_publication_on_the_same_port()
    {
        var transport = new Control();
        using var controller = new SharedSshPortController(transport, [40000]);
        var old = await controller.PublishAsync(Guid.NewGuid(), 8080, default);
        await controller.RevokeAsync(old, default);
        var current = await controller.PublishAsync(Guid.NewGuid(), 8080, default);
        await Assert.That(await controller.RevokeAsync(old, default)).IsFalse();
        await Assert.That(transport.Bindings[40000]).IsEqualTo(current);
    }

    [Test]
    public async Task Reconnect_preserves_port_assignment_but_invalidates_old_session_handles()
    {
        var transport = new Control();
        using var controller = new SharedSshPortController(transport, [40000]);
        var id = Guid.NewGuid();
        var old = await controller.PublishAsync(id, 8080, default);
        transport.Restart();
        var current = await controller.PublishAsync(id, 8080, default);
        await Assert.That(current.PublicPort).IsEqualTo(old.PublicPort);
        await Assert.That(current.SessionId).IsNotEqualTo(old.SessionId);
        await Assert.That(await controller.RevokeAsync(old, default)).IsFalse();
        await Assert.That(transport.Bindings[40000]).IsEqualTo(current);
    }

    [Test]
    public async Task Uncertain_publish_retains_reservation_and_can_be_cleaned_by_publication()
    {
        var transport = new Control { ThrowAfterPublish = true };
        using var controller = new SharedSshPortController(transport, [40000]);
        var id = Guid.NewGuid();
        await Assert.That(async () => { await controller.PublishAsync(id, 8080, default); }).Throws<OperationCanceledException>();
        transport.ThrowAfterPublish = false;
        await Assert.That(async () => { await controller.PublishAsync(Guid.NewGuid(), 8080, default); }).Throws<PublicTunnelException>();
        await controller.RevokePublicationAsync(id, default);
        await Assert.That(transport.Bindings).IsEmpty();
        await Assert.That((await controller.PublishAsync(Guid.NewGuid(), 8080, default)).PublicPort).IsEqualTo(40000);
    }

    [Test]
    public async Task Failed_cancel_and_missing_master_do_not_release_the_port_for_reuse()
    {
        var transport = new Control();
        using var controller = new SharedSshPortController(transport, [40000]);
        var lease = await controller.PublishAsync(Guid.NewGuid(), 8080, default);
        transport.RejectCancellation = true;
        await Assert.That(() => controller.RevokeAsync(lease, default)).Throws<PublicTunnelException>();
        transport.Unavailable = true;
        await Assert.That(() => controller.RevokeAsync(lease, default)).Throws<PublicTunnelException>();
        transport.Unavailable = false;
        await Assert.That(async () => { await controller.PublishAsync(Guid.NewGuid(), 8080, default); }).Throws<PublicTunnelException>();
        transport.Restart();
        await Assert.That(await controller.RevokeAsync(lease, default)).IsTrue();
        await Assert.That((await controller.PublishAsync(Guid.NewGuid(), 8080, default)).PublicPort).IsEqualTo(40000);
    }

    [Test]
    public async Task Session_change_during_publish_is_not_reported_as_ready()
    {
        var transport = new Control { RestartAfterPublish = true };
        using var controller = new SharedSshPortController(transport, [40000]);
        await Assert.That(async () => { await controller.PublishAsync(Guid.NewGuid(), 8080, default); }).Throws<PublicTunnelException>();
        await Assert.That(transport.Bindings).IsEmpty();
    }

    [Test]
    public async Task Confirmed_absent_publication_releases_a_failed_publish_reservation()
    {
        var transport = new Control { RejectPublish = true };
        using var controller = new SharedSshPortController(transport, [40000]);
        var publication = Guid.NewGuid();
        await Assert.That(async () => { await controller.PublishAsync(publication, 8080, default); }).Throws<PublicTunnelException>();
        transport.ReportAbsent = true;
        await controller.RevokePublicationAsync(publication, default);
        transport.RejectPublish = false;
        await Assert.That((await controller.PublishAsync(Guid.NewGuid(), 8080, default)).PublicPort).IsEqualTo(40000);
    }

    private sealed class Control : ISharedSshControl
    {
        private string session = Guid.NewGuid().ToString();
        private int active;
        public int MaximumConcurrentCommands { get; private set; }
        public bool ThrowAfterPublish { get; set; }
        public bool RestartAfterPublish { get; set; }
        public bool RejectCancellation { get; set; }
        public bool Unavailable { get; set; }
        public bool RejectPublish { get; set; }
        public bool ReportAbsent { get; set; }
        public Dictionary<int, SshPortLease> Bindings { get; } = [];
        public Task<string?> ReadSessionAsync(CancellationToken ct) => Task.FromResult(Unavailable ? null : session);
        public void Restart() { session = Guid.NewGuid().ToString(); Bindings.Clear(); }
        public async Task<SshRevocationResult> RevokeAsync(SshPortLease lease, CancellationToken ct) => ReportAbsent
            ? SshRevocationResult.AlreadyAbsent
            : await ForwardAsync(lease, SshForwardOperation.Revoke, ct) ? SshRevocationResult.Applied : SshRevocationResult.Uncertain;
        public async Task<bool> ForwardAsync(SshPortLease lease, SshForwardOperation operation, CancellationToken ct)
        {
            MaximumConcurrentCommands = Math.Max(MaximumConcurrentCommands, Interlocked.Increment(ref active));
            try
            {
                await Task.Yield();
                if (operation == SshForwardOperation.Revoke)
                    return !RejectCancellation && Bindings.Remove(lease.PublicPort);
                if (RejectPublish) return false;
                Bindings[lease.PublicPort] = lease;
                if (RestartAfterPublish) Restart();
                if (ThrowAfterPublish) throw new OperationCanceledException("Synthetic lost acknowledgment.");
                return true;
            }
            finally { Interlocked.Decrement(ref active); }
        }
    }
}
