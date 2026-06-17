using Microsoft.AspNetCore.SignalR;
using NoCTF.API.SignalR;

namespace NoCTF.API.Logging;

/// <summary>
/// Thread-safe in-memory circular buffer for log entries.
/// Broadcasts new entries to all MonitorHub connections via SignalR.
/// </summary>
public sealed class LogBuffer
{
    private const int Capacity = 500;
    private static readonly string[] RedactKeywords =
        ["jwt", "secret", "password", "token", "flag{"];

    private readonly LogEntryDto[] _buffer = new LogEntryDto[Capacity];
    private int _head;
    private int _count;
    private readonly object _lock = new();

    // Lazily resolved to avoid circular DI at construction time
    private IHubContext<MonitorHub, IMonitorClient>? _hubContext;

    public void SetHubContext(IHubContext<MonitorHub, IMonitorClient> hubContext)
        => _hubContext = hubContext;

    public void Enqueue(LogEntryDto entry)
    {
        lock (_lock)
        {
            _buffer[_head] = entry;
            _head = (_head + 1) % Capacity;
            if (_count < Capacity) _count++;
        }

        // Fire-and-forget broadcast — do not block the caller
        _ = BroadcastAsync(entry);
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

    private async Task BroadcastAsync(LogEntryDto entry)
    {
        if (_hubContext is null) return;
        try
        {
            await _hubContext.Clients.All.ReceiveLogEntry(entry);
        }
        catch
        {
            // Never let broadcast errors propagate into the logging pipeline
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
