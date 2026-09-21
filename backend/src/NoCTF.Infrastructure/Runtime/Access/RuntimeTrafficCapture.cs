using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NoCTF.Application.Competitions.Events;
using NoCTF.Application.Messaging;
using NoCTF.Application.Runtime.Access;
using NoCTF.Application.Storage;
using NoCTF.Domain.Competitions.Events;
using NoCTF.Domain.Shared;
using NoCTF.Infrastructure.Persistence;

namespace NoCTF.Infrastructure.Runtime.Access;

public sealed class RuntimeTrafficCaptureFactory(
    NoCtfDbContext db,
    ManagedFileUploads uploads,
    ICompetitionEventRecorder events,
    ITransactionalMessageOutbox outbox,
    RuntimeProxyOptions options,
    TimeProvider timeProvider,
    ILogger<RuntimeTrafficCaptureFactory> logger) : IRuntimeTrafficCaptureFactory
{
    public Task<IRuntimeTrafficCaptureSession?> CreateAsync(
        RuntimeProxyTarget target,
        IPEndPoint client,
        IPEndPoint destination,
        string connectionId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!target.TrafficCaptureEnabled
            || target.CompetitionId is null
            || target.CompetitionChallengeId is null)
        {
            return Task.FromResult<IRuntimeTrafficCaptureSession?>(null);
        }
        var limit = Math.Min(
            target.TrafficCaptureLimitBytes ?? options.DefaultCaptureLimitBytes,
            options.MaximumCaptureLimitBytes);
        return Task.FromResult<IRuntimeTrafficCaptureSession?>(new Session(
            db,
            uploads,
            events,
            outbox,
            timeProvider,
            logger,
            target,
            client,
            destination,
            connectionId,
            limit));
    }

    private sealed class Session : IRuntimeTrafficCaptureSession
    {
        private const int ReservationBlockBytes = 1_048_576;
        private const int MaximumPacketPayloadBytes = 60_000;
        private readonly NoCtfDbContext db;
        private readonly ManagedFileUploads uploads;
        private readonly ICompetitionEventRecorder events;
        private readonly ITransactionalMessageOutbox outbox;
        private readonly TimeProvider timeProvider;
        private readonly ILogger logger;
        private readonly RuntimeProxyTarget target;
        private readonly IPEndPoint client;
        private readonly IPEndPoint destination;
        private readonly string connectionId;
        private readonly long limit;
        private readonly string temporaryPath;
        private readonly FileStream stream;
        private readonly ConcurrentQueue<CaptureRecord> queue = new();
        private readonly object queueSync = new();
        private readonly SemaphoreSlim queued = new(0);
        private readonly Task writer;
        private readonly DateTimeOffset startedAt;
        private long availableReservation;
        private long usedReservation;
        private long clientToRuntimeBytes;
        private long runtimeToClientBytes;
        private int truncated;
        private int disposed;
        private int queuedCount;
        private int completing;
        private uint clientSequence = 1;
        private uint runtimeSequence = 1;

        internal Session(
            NoCtfDbContext db,
            ManagedFileUploads uploads,
            ICompetitionEventRecorder events,
            ITransactionalMessageOutbox outbox,
            TimeProvider timeProvider,
            ILogger logger,
            RuntimeProxyTarget target,
            IPEndPoint client,
            IPEndPoint destination,
            string connectionId,
            long limit)
        {
            this.db = db;
            this.uploads = uploads;
            this.events = events;
            this.outbox = outbox;
            this.timeProvider = timeProvider;
            this.logger = logger;
            this.target = target;
            this.client = client;
            this.destination = destination;
            this.connectionId = connectionId;
            this.limit = limit;
            startedAt = timeProvider.GetUtcNow();
            temporaryPath = Path.Combine(
                Path.GetTempPath(),
                $"noctf-runtime-capture-{Guid.NewGuid():N}.pcapng");
            stream = new FileStream(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.ReadWrite,
                FileShare.Read,
                bufferSize: 81_920,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            writer = WriteAsync();
        }

        public ValueTask RecordAsync(
            RuntimeTrafficDirection direction,
            ReadOnlyMemory<byte> payload,
            CancellationToken cancellationToken)
        {
            if (payload.Length == 0)
                return ValueTask.CompletedTask;
            cancellationToken.ThrowIfCancellationRequested();
            if (direction == RuntimeTrafficDirection.ClientToRuntime)
                Interlocked.Add(ref clientToRuntimeBytes, payload.Length);
            else
                Interlocked.Add(ref runtimeToClientBytes, payload.Length);

            var remaining = payload;
            while (!remaining.IsEmpty)
            {
                var length = Math.Min(remaining.Length, MaximumPacketPayloadBytes);
                var data = remaining[..length].ToArray();
                remaining = remaining[length..];
                lock (queueSync)
                {
                    if (disposed != 0)
                        return ValueTask.CompletedTask;
                    if (queuedCount >= 256)
                    {
                        Interlocked.Exchange(ref truncated, 1);
                        continue;
                    }
                    queuedCount++;
                    queue.Enqueue(new(
                        direction,
                        data,
                        timeProvider.GetUtcNow()));
                    queued.Release();
                }
            }
            return ValueTask.CompletedTask;
        }

        public async ValueTask DisposeAsync()
        {
            lock (queueSync)
            {
                if (disposed != 0)
                    return;
                disposed = 1;
                completing = 1;
            }
            queued.Release();
            try
            {
                await writer;
                await RefundUnusedReservationAsync();
                if (usedReservation <= 0)
                    return;
                stream.Position = 0;
                var now = timeProvider.GetUtcNow();
                var fileId = Guid.CreateVersion7(now);
                var segmentId = Guid.CreateVersion7(now);
                var fileName = $"runtime-{target.RuntimeInstanceId:N}-{segmentId:N}.pcapng";
                var upload = await uploads.CreateAsync(
                    fileId,
                    $"traffic-captures/{target.CompetitionId!.Value:N}/{target.RuntimeInstanceId:N}/{fileName}",
                    fileName,
                    "application/vnd.tcpdump.pcap",
                    stream,
                    now,
                    CancellationToken.None);
                var payload = JsonSerializer.Serialize(new
                {
                    schemaVersion = 1,
                    segmentId,
                    target.BindingIndex,
                    connectionId,
                    startedAt,
                    endedAt = now,
                    clientAddress = client.Address.ToString(),
                    clientPort = client.Port,
                    destinationAddress = destination.Address.ToString(),
                    destinationPort = destination.Port,
                    clientToRuntimeBytes,
                    runtimeToClientBytes,
                    capturedBytes = upload.ByteLength,
                    truncated = Volatile.Read(ref truncated) != 0
                });
                await events.RecordAsync(new(
                    target.CompetitionId.Value,
                    CompetitionEventKind.RuntimeTrafficCaptureStored,
                    Volatile.Read(ref truncated) != 0
                        ? CompetitionEventLevel.Warning
                        : CompetitionEventLevel.Information,
                    CompetitionEventVisibility.Staff,
                    startedAt,
                    TeamId: target.TeamId,
                    CompetitionChallengeId: target.CompetitionChallengeId,
                    RuntimeInstanceId: target.RuntimeInstanceId,
                    SubjectType: EntityReferenceKind.RuntimeInstance,
                    SubjectId: target.RuntimeInstanceId,
                    RelatedType: EntityReferenceKind.File,
                    RelatedId: fileId,
                    PayloadJson: payload), CancellationToken.None);
                await db.SaveChangesAsync(CancellationToken.None);
                await outbox.FlushOutgoingMessagesAsync();
            }
            catch (Exception exception)
            {
                logger.LogWarning(
                    exception,
                    "Runtime traffic capture finalization failed for Runtime {RuntimeInstanceId}.",
                    target.RuntimeInstanceId);
            }
            finally
            {
                await stream.DisposeAsync();
                queued.Dispose();
                try
                {
                    File.Delete(temporaryPath);
                }
                catch (IOException)
                {
                    // Temporary cleanup is best effort.
                }
            }
        }

        private async Task<bool> EnsureReservationAsync(
            int requiredBytes,
            CancellationToken cancellationToken)
        {
            if (availableReservation >= requiredBytes)
                return true;
            var desired = Math.Max(ReservationBlockBytes, requiredBytes);
            if (await TryReserveAsync(desired, cancellationToken))
            {
                availableReservation += desired;
                return true;
            }
            var reserved = await db.RuntimeInstances.AsNoTracking()
                .Where(runtime => runtime.Id == target.RuntimeInstanceId)
                .Select(runtime => runtime.TrafficCaptureReservedBytes)
                .SingleOrDefaultAsync(cancellationToken);
            var remainder = Math.Max(0, limit - reserved);
            if (remainder < requiredBytes
                || !await TryReserveAsync(remainder, cancellationToken))
                return false;
            availableReservation += remainder;
            return true;
        }

        private async Task<bool> TryReserveAsync(
            long bytes,
            CancellationToken cancellationToken)
        {
            if (bytes <= 0)
                return false;
            db.ChangeTracker.Clear();
            return await db.RuntimeInstances
                .Where(runtime => runtime.Id == target.RuntimeInstanceId
                    && runtime.TrafficCaptureEnabled
                    && runtime.TrafficCaptureReservedBytes + bytes <= limit)
                .ExecuteUpdateAsync(setters => setters.SetProperty(
                    runtime => runtime.TrafficCaptureReservedBytes,
                    runtime => runtime.TrafficCaptureReservedBytes + bytes),
                    cancellationToken) == 1;
        }

        private async Task RefundUnusedReservationAsync()
        {
            if (availableReservation <= 0)
                return;
            db.ChangeTracker.Clear();
            _ = await db.RuntimeInstances
                .Where(runtime => runtime.Id == target.RuntimeInstanceId
                    && runtime.TrafficCaptureReservedBytes >= availableReservation)
                .ExecuteUpdateAsync(setters => setters.SetProperty(
                    runtime => runtime.TrafficCaptureReservedBytes,
                    runtime => runtime.TrafficCaptureReservedBytes - availableReservation),
                    CancellationToken.None);
            availableReservation = 0;
        }

        private async Task WriteAsync()
        {
            await PcapNgWriter.WriteHeaderAsync(stream, CancellationToken.None);
            while (true)
            {
                await queued.WaitAsync(CancellationToken.None);
                while (queue.TryDequeue(out var record))
                {
                    lock (queueSync)
                        queuedCount--;
                    var encodedLength = PcapNgWriter.EncodedPacketLength(record.Payload.Length)
                        + (usedReservation == 0 ? PcapNgWriter.HeaderLength : 0);
                    if (!await EnsureReservationAsync(
                            encodedLength,
                            CancellationToken.None))
                    {
                        Interlocked.Exchange(ref truncated, 1);
                        continue;
                    }
                    availableReservation -= encodedLength;
                    usedReservation += encodedLength;
                    var source = record.Direction == RuntimeTrafficDirection.ClientToRuntime
                        ? client
                        : destination;
                    var destinationEndpoint = record.Direction == RuntimeTrafficDirection.ClientToRuntime
                        ? destination
                        : client;
                    var sequence = record.Direction == RuntimeTrafficDirection.ClientToRuntime
                        ? clientSequence
                        : runtimeSequence;
                    var acknowledgment = record.Direction == RuntimeTrafficDirection.ClientToRuntime
                        ? runtimeSequence
                        : clientSequence;
                    await PcapNgWriter.WritePacketAsync(
                        stream,
                        source,
                        destinationEndpoint,
                        record.Payload,
                        record.Timestamp,
                        sequence,
                        acknowledgment,
                        CancellationToken.None);
                    if (record.Direction == RuntimeTrafficDirection.ClientToRuntime)
                    {
                        clientSequence = unchecked(
                            clientSequence + (uint)record.Payload.Length);
                    }
                    else
                    {
                        runtimeSequence = unchecked(
                            runtimeSequence + (uint)record.Payload.Length);
                    }
                }
                lock (queueSync)
                {
                    if (completing != 0 && queue.IsEmpty)
                        break;
                }
            }
            await stream.FlushAsync(CancellationToken.None);
        }

        private sealed record CaptureRecord(
            RuntimeTrafficDirection Direction,
            byte[] Payload,
            DateTimeOffset Timestamp);
    }
}

internal static class PcapNgWriter
{
    internal const int HeaderLength = 48;
    private const uint SectionHeaderBlock = 0x0A0D0D0A;
    private const uint InterfaceDescriptionBlock = 1;
    private const uint EnhancedPacketBlock = 6;
    private const ushort EthernetLinkType = 1;
    private const int PacketHeaderBytes = 14 + 40 + 20;

    internal static int EncodedPacketLength(int payloadLength)
    {
        var packetLength = checked(PacketHeaderBytes + payloadLength);
        return checked(32 + Align4(packetLength));
    }

    internal static async Task WriteHeaderAsync(
        Stream stream,
        CancellationToken cancellationToken)
    {
        var section = new byte[28];
        BinaryPrimitives.WriteUInt32LittleEndian(section, SectionHeaderBlock);
        BinaryPrimitives.WriteUInt32LittleEndian(section.AsSpan(4), 28);
        BinaryPrimitives.WriteUInt32LittleEndian(section.AsSpan(8), 0x1A2B3C4D);
        BinaryPrimitives.WriteUInt16LittleEndian(section.AsSpan(12), 1);
        BinaryPrimitives.WriteUInt16LittleEndian(section.AsSpan(14), 0);
        BinaryPrimitives.WriteInt64LittleEndian(section.AsSpan(16), -1);
        BinaryPrimitives.WriteUInt32LittleEndian(section.AsSpan(24), 28);
        await stream.WriteAsync(section, cancellationToken);

        var descriptor = new byte[20];
        BinaryPrimitives.WriteUInt32LittleEndian(descriptor, InterfaceDescriptionBlock);
        BinaryPrimitives.WriteUInt32LittleEndian(descriptor.AsSpan(4), 20);
        BinaryPrimitives.WriteUInt16LittleEndian(descriptor.AsSpan(8), EthernetLinkType);
        BinaryPrimitives.WriteUInt32LittleEndian(descriptor.AsSpan(12), uint.MaxValue);
        BinaryPrimitives.WriteUInt32LittleEndian(descriptor.AsSpan(16), 20);
        await stream.WriteAsync(descriptor, cancellationToken);
    }

    internal static async Task WritePacketAsync(
        Stream stream,
        IPEndPoint source,
        IPEndPoint destination,
        ReadOnlyMemory<byte> payload,
        DateTimeOffset timestamp,
        uint sequence,
        uint acknowledgment,
        CancellationToken cancellationToken)
    {
        var packet = BuildPacket(
            source,
            destination,
            payload.Span,
            sequence,
            acknowledgment);
        var paddedLength = Align4(packet.Length);
        var blockLength = checked(32 + paddedLength);
        var block = new byte[blockLength];
        BinaryPrimitives.WriteUInt32LittleEndian(block, EnhancedPacketBlock);
        BinaryPrimitives.WriteUInt32LittleEndian(block.AsSpan(4), (uint)blockLength);
        var micros = checked((ulong)(timestamp - DateTimeOffset.UnixEpoch).Ticks / 10);
        BinaryPrimitives.WriteUInt32LittleEndian(block.AsSpan(12), (uint)(micros >> 32));
        BinaryPrimitives.WriteUInt32LittleEndian(block.AsSpan(16), (uint)micros);
        BinaryPrimitives.WriteUInt32LittleEndian(block.AsSpan(20), (uint)packet.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(block.AsSpan(24), (uint)packet.Length);
        packet.CopyTo(block.AsSpan(28));
        BinaryPrimitives.WriteUInt32LittleEndian(block.AsSpan(blockLength - 4), (uint)blockLength);
        await stream.WriteAsync(block, cancellationToken);
    }

    private static byte[] BuildPacket(
        IPEndPoint source,
        IPEndPoint destination,
        ReadOnlySpan<byte> payload,
        uint sequence,
        uint acknowledgment)
    {
        var packet = new byte[PacketHeaderBytes + payload.Length];
        packet[0] = 0x00;
        packet[1] = 0x11;
        packet[6] = 0x00;
        packet[7] = 0x22;
        BinaryPrimitives.WriteUInt16BigEndian(packet.AsSpan(12), 0x86DD);
        var ipv6 = packet.AsSpan(14);
        ipv6[0] = 0x60;
        BinaryPrimitives.WriteUInt16BigEndian(
            ipv6[4..],
            checked((ushort)(20 + payload.Length)));
        ipv6[6] = 6;
        ipv6[7] = 64;
        source.Address.MapToIPv6().GetAddressBytes().CopyTo(ipv6[8..24]);
        destination.Address.MapToIPv6().GetAddressBytes().CopyTo(ipv6[24..40]);
        var tcp = ipv6[40..];
        BinaryPrimitives.WriteUInt16BigEndian(tcp, checked((ushort)source.Port));
        BinaryPrimitives.WriteUInt16BigEndian(tcp[2..], checked((ushort)destination.Port));
        BinaryPrimitives.WriteUInt32BigEndian(tcp[4..], sequence);
        BinaryPrimitives.WriteUInt32BigEndian(tcp[8..], acknowledgment);
        tcp[12] = 5 << 4;
        tcp[13] = 0x18;
        BinaryPrimitives.WriteUInt16BigEndian(tcp[14..], ushort.MaxValue);
        payload.CopyTo(tcp[20..]);
        BinaryPrimitives.WriteUInt16BigEndian(
            tcp[16..],
            TransportChecksum(ipv6[8..24], ipv6[24..40], tcp, 6));
        return packet;
    }

    private static ushort TransportChecksum(
        ReadOnlySpan<byte> source,
        ReadOnlySpan<byte> destination,
        ReadOnlySpan<byte> transport,
        byte nextHeader)
    {
        uint sum = 0;
        Sum(source);
        Sum(destination);
        sum += (uint)transport.Length;
        sum += nextHeader;
        Sum(transport);
        while (sum >> 16 != 0)
            sum = (sum & 0xFFFF) + (sum >> 16);
        var result = (ushort)~sum;
        return result == 0 ? (ushort)0xFFFF : result;

        void Sum(ReadOnlySpan<byte> bytes)
        {
            for (var index = 0; index < bytes.Length; index += 2)
            {
                sum += index + 1 < bytes.Length
                    ? BinaryPrimitives.ReadUInt16BigEndian(bytes[index..])
                    : (uint)(bytes[index] << 8);
            }
        }
    }

    private static int Align4(int value) => checked((value + 3) & ~3);
}
