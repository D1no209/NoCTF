using Microsoft.Extensions.Logging;
using NoCTF.API.SignalR;

namespace NoCTF.API.Logging;

/// <summary>
/// Forwards Microsoft.Extensions.Logging events (>= Information) to <see cref="LogBuffer"/>.
/// </summary>
internal sealed class LogStreamerLogger(string categoryName, LogBuffer buffer) : ILogger
{
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Information;

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        if (!IsEnabled(logLevel)) return;

        var rawMessage = formatter(state, exception);
        var message = LogBuffer.Redact(rawMessage);

        // Trim category to last segment (e.g. "NoCTF.API.Endpoints.Admin.GetUsersEndpoint" → "GetUsersEndpoint")
        var source = categoryName.Contains('.')
            ? categoryName[(categoryName.LastIndexOf('.') + 1)..]
            : categoryName;

        var level = logLevel switch
        {
            LogLevel.Warning => "Warning",
            LogLevel.Error => "Error",
            LogLevel.Critical => "Error",
            _ => "Information"
        };

        var entry = new LogEntryDto(level, message, source, DateTimeOffset.UtcNow);
        buffer.Enqueue(entry);
    }
}
