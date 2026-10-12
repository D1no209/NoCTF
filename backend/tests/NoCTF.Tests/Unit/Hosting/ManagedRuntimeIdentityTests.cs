using NoCTF.Hosting.ManagedRuntime;

namespace NoCTF.Tests.Unit.Hosting;

public sealed class ManagedRuntimeIdentityTests
{
    [Test]
    public async Task Collector_detection_requires_the_actual_boolean_marker()
    {
        await Assert.That(ManagedRuntimeIdentity.Detect(new Dictionary<string, object>())).IsEqualTo(ManagedRuntimeFlavor.Official);
        await Assert.That(ManagedRuntimeIdentity.Detect(new Dictionary<string, object> { ["SatoriGC"] = true })).IsEqualTo(ManagedRuntimeFlavor.Satori);
        await Assert.That(ManagedRuntimeIdentity.Detect(new Dictionary<string, object> { ["SatoriGC"] = "true" })).IsEqualTo(ManagedRuntimeFlavor.Official);
        await Assert.That(ManagedRuntimeIdentity.Detect(new Dictionary<string, object> { ["SatoriGC"] = false })).IsEqualTo(ManagedRuntimeFlavor.Official);
    }

    [Test]
    [Arguments(ManagedRuntimeFlavor.Official, ManagedRuntimeFlavor.Satori)]
    [Arguments(ManagedRuntimeFlavor.Satori, ManagedRuntimeFlavor.Official)]
    public async Task Unexpected_runtime_cannot_start(ManagedRuntimeFlavor expected, ManagedRuntimeFlavor actual)
    {
        var identity = new ManagedRuntimeIdentity(expected, actual, ".NET", "10.0.12", "linux-x64", "test");
        await Assert.That(() => identity.Validate()).Throws<InvalidOperationException>();
    }

    [Test]
    public async Task Runtime_info_uses_bounded_identity_and_has_no_host_configuration()
    {
        var identity = new ManagedRuntimeIdentity(ManagedRuntimeFlavor.Satori, ManagedRuntimeFlavor.Satori,
            ".NET 10.0.12", "10.0.12", "linux-x64", "test");
        identity.Validate();
        await Assert.That(identity.ToJson()).Contains("\"actualFlavor\":\"Satori\"");
        await Assert.That(identity.ToJson()).DoesNotContain("ConnectionStrings");
    }
}
