using NoCTF.Domain.Runtime;

namespace NoCTF.Tests.Unit.Runtime;

public sealed class RuntimeAccessUrlTests
{
    [Test]
    [Arguments("http://runtime.example:31001/path", RuntimeAccessScheme.Http)]
    [Arguments("https://runtime.example/path", RuntimeAccessScheme.Https)]
    [Arguments("tcp://runtime.example:31002", RuntimeAccessScheme.Tcp)]
    [Arguments("udp://runtime.example:31003", RuntimeAccessScheme.Udp)]
    [Arguments("ssh://user:password@runtime.example:31004", RuntimeAccessScheme.Ssh)]
    public async Task Supported_access_schemes_are_normalized(
        string value,
        RuntimeAccessScheme expectedScheme)
    {
        var accepted = RuntimeAccessUrl.TryCreate($"  {value}  ", out var accessUrl);

        await Assert.That(accepted).IsTrue();
        await Assert.That(accessUrl.Scheme).IsEqualTo(expectedScheme);
        await Assert.That(accessUrl.Value).IsEqualTo(new Uri(value).AbsoluteUri);
    }

    [Test]
    [Arguments("javascript:alert(1)")]
    [Arguments("data:text/html,unsafe")]
    [Arguments("file:///etc/passwd")]
    [Arguments("mailto:operator@example.test")]
    [Arguments("http:relative")]
    [Arguments("relative/path")]
    public async Task Unsupported_or_non_authority_urls_are_rejected(string value)
    {
        var accepted = RuntimeAccessUrl.TryCreate(value, out _);

        await Assert.That(accepted).IsFalse();
    }
}
