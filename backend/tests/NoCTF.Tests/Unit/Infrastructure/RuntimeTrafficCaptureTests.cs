using System.Buffers.Binary;
using System.Net;
using NoCTF.Application.Runtime.Access;
using NoCTF.Infrastructure.Runtime.Access;

namespace NoCTF.Tests.Unit.Infrastructure;

public sealed class RuntimeTrafficCaptureTests
{
    [Test]
    public async Task Pcapng_sections_are_self_contained_and_concatenable()
    {
        var first = await SegmentAsync("hello"u8.ToArray());
        var second = await SegmentAsync("world"u8.ToArray());
        var combined = first.Concat(second).ToArray();

        await Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(first))
            .IsEqualTo(0x0A0D0D0Au);
        await Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(first.AsSpan(28)))
            .IsEqualTo(1u);
        await Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(first.AsSpan(48)))
            .IsEqualTo(6u);
        await Assert.That(first[96]).IsEqualTo((byte)6);
        await Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(combined.AsSpan(first.Length)))
            .IsEqualTo(0x0A0D0D0Au);
    }

    [Test]
    public async Task Local_connection_gate_enforces_the_runtime_limit()
    {
        var gate = new RuntimeProxyConnectionGate(
            new RuntimeProxyOptions(MaximumConnectionsPerRuntime: 2));
        var runtimeId = Guid.CreateVersion7();
        await using var first = await gate.TryAcquireAsync(runtimeId, CancellationToken.None);
        await using var second = await gate.TryAcquireAsync(runtimeId, CancellationToken.None);
        var denied = await gate.TryAcquireAsync(runtimeId, CancellationToken.None);

        await Assert.That(first).IsNotNull();
        await Assert.That(second).IsNotNull();
        await Assert.That(denied).IsNull();
    }

    private static async Task<byte[]> SegmentAsync(byte[] payload)
    {
        await using var stream = new MemoryStream();
        await PcapNgWriter.WriteHeaderAsync(stream, CancellationToken.None);
        await PcapNgWriter.WritePacketAsync(
            stream,
            new IPEndPoint(IPAddress.Parse("192.0.2.10"), 40000),
            new IPEndPoint(IPAddress.Parse("198.51.100.20"), 31337),
            payload,
            DateTimeOffset.UnixEpoch.AddSeconds(1),
            sequence: 1,
            acknowledgment: 1,
            CancellationToken.None);
        return stream.ToArray();
    }
}
