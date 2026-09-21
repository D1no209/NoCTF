using NoCTF.Application.Competitions.Management;
using NoCTF.Domain.Runtime;

namespace NoCTF.Tests.Unit.Application;

public sealed class RuntimeAccessPolicyTests
{
    [Test]
    public async Task Capture_requires_wsrx_and_accepts_bounded_override()
    {
        await Assert.That(RuntimeAccessPolicy.IsValid(
            RuntimeAccessMode.Direct,
            trafficCaptureEnabled: true,
            trafficCaptureLimitBytes: null)).IsFalse();
        await Assert.That(RuntimeAccessPolicy.IsValid(
            RuntimeAccessMode.DirectAndWsrx,
            trafficCaptureEnabled: true,
            trafficCaptureLimitBytes: 256 * 1024 * 1024)).IsTrue();
        await Assert.That(RuntimeAccessPolicy.IsValid(
            RuntimeAccessMode.WsrxOnly,
            trafficCaptureEnabled: true,
            trafficCaptureLimitBytes: RuntimeAccessPolicy.MaximumCaptureLimitBytes + 1)).IsFalse();
    }
}
