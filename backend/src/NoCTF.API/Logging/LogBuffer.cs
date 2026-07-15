using Microsoft.AspNetCore.SignalR;
using NoCTF.API.SignalR;
using System.Threading.Channels;

namespace NoCTF.API.Logging;

/// <summary>
/// Thread-safe in-memory circular buffer for log entries.
/// Broadcasts new entries only to admin monitor clients via SignalR.
/// </summary>
public sealed class LogBuffer : IAsyncDisposable
{
    private const int Capacity = 500;
    internal const int DefaultBroadcastCapacity = 256;
    private static readonly string[] RedactKeywords =
        ["jwt", "secret", "password", "token", "flag{"];

    private readonly LogEntryDto[] _buffer = new LogEntryDto[Capacity];
    private int _head;
    private int _count;
    private readonly object _lock = new();
    private readonly Channel<LogEntryDto> _broadcastQueue;
    private readonly CancellationTokenSource _shutdown = new();
    private readonly Task _broadcastWorker;
    private readonly Func<LogEntryDto, CancellationToken, Task>? _broadcastOverride;
    private long _droppedBroadcasts;

    // Lazily resolved to avoid circular DI at construction time
    private IHubContext<MonitorHub, IMonitorClient>? _hubContext;

    public LogBuffer()
        : this(DefaultBroadcastCapacity)
    {
    }

    internal LogBuffer(
        int broadcastCapacity,
        Func<LogEntryDto, CancellationToken, Task>? broadcastOverride = null)
    {
        if (broadcastCapacity <= 0)
            throw new ArgumentOutOfRangeException(nameof(broadcastCapacity));

        _broadcastOverride = broadcastOverride;
        _broadcastQueue = Channel.CreateBounded<LogEntryDto>(new BoundedChannelOptions(broadcastCapacity)
        {
            AllowSynchronousContinuations = false,
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = false
        });
        _broadcastWorker = ProcessBroadcastQueueAsync(_shutdown.Token);
    }

    internal long DroppedBroadcastCount => Interlocked.Read(ref _droppedBroadcasts);

    public void SetHubContext(IHubContext<MonitorHub, IMonitorClient> hubContext)
        => Volatile.Write(ref _hubContext, hubContext);

    public void Enqueue(LogEntryDto entry)
    {
        lock (_lock)
        {
            _buffer[_head] = entry;
            _head = (_head + 1) % Capacity;
            if (_count < Capacity) _count++;
        }

        // Logging must stay synchronous, so overload is shed instead of creating
        // an unbounded number of fire-and-forget SignalR tasks.
        if (!_broadcastQueue.Writer.TryWrite(entry))
            Interlocked.Increment(ref _droppedBroadcasts);
    }

    public IReadOnlyList<LogEntryDto> GetRecent(int count = 100)
    {
        lock (_lock)
        {
            var take = Math.Min(count, _count);
            var result = new LogEntryDto[take];
            // Walk backwards from the most-recent slot
            var start = ((_head - _count + Capacity) % Capacity);
            for (var i = 0; i < take; i++)
            {
                result[i] = _buffer[(start + (_count - take) + i) % Capacity];
            }
            return result;
        }
    }

    private async Task ProcessBroadcastQueueAsync(CancellationToken ct)
    {
        try
        {
            await foreach (var entry in _broadcastQueue.Reader.ReadAllAsync(ct))
            {
                try
                {
                    if (_broadcastOverride is not null)
                    {
                        await _broadcastOverride(entry, ct);
                        continue;
                    }

                    var hubContext = Volatile.Read(ref _hubContext);
                    if (hubContext is not null)
                    {
                        await hubContext.Clients
                            .Group(MonitorHub.AdminLogGroup)
                            .ReceiveLogEntry(entry);
                    }
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    return;
                }
                catch
                {
                    // Never let broadcast errors terminate the bounded consumer.
                }
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
        }
    }

    public async ValueTask DisposeAsync()
    {
        _broadcastQueue.Writer.TryComplete();
        await _shutdown.CancelAsync();
        try
        {
            await _broadcastWorker;
        }
        catch (OperationCanceledException) when (_shutdown.IsCancellationRequested)
        {
        }
        finally
        {
            _shutdown.Dispose();
        }
    }

    public static string Redact(string message)
    {
        foreach (var kw in RedactKeywords)
        {
            if (message.Contains(kw, StringComparison.OrdinalIgnoreCase))
                return "[REDACTED]";
        }
        return message;
    }
}
