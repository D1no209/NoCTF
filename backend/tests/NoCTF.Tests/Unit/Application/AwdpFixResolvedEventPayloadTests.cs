using NoCTF.Application.Competitions.Events;
using NoCTF.Domain.Gameplay;

namespace NoCTF.Tests.Unit.Application;

public sealed class AwdpFixResolvedEventPayloadTests
{
    [Test]
    public async Task Payload_round_trips_the_bounded_result_without_sensitive_diagnostics()
    {
        var payload = AwdpFixResolvedEventPayload.Create(
            Guid.Parse("019fdd17-d997-7c02-9109-9aa04563e3d7"),
            Guid.Parse("019fdd17-d997-7c02-9109-9aa04563e3d8"),
            Guid.Parse("019fdd17-d997-7c02-9109-9aa04563e3d9"),
            Guid.Parse("019fdd17-d997-7c02-9109-9aa04563e3da"),
            Guid.Parse("019fdd17-d997-7c02-9109-9aa04563e3db"),
            AwdpFixOutcome.PlatformFailed,
            GameplayFactFailureCode.CheckerPlatformError,
            new DateTimeOffset(2026, 8, 24, 8, 30, 0, TimeSpan.Zero));

        var json = payload.Serialize();
        var restored = AwdpFixResolvedEventPayload.Deserialize(json);

        await Assert.That(restored).IsEqualTo(payload);
        await Assert.That(json).Contains("\"schemaVersion\":1");
        await Assert.That(json).Contains("\"outcome\":\"PlatformFailed\"");
        await Assert.That(json).DoesNotContain("flag");
        await Assert.That(json).DoesNotContain("token");
        await Assert.That(json).DoesNotContain("password");
        await Assert.That(json).DoesNotContain("stderr");
    }
}
