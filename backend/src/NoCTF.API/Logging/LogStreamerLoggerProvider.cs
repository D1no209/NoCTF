using Microsoft.Extensions.Logging;

namespace NoCTF.API.Logging;

/// <summary>
/// ILoggerProvider that routes log events to <see cref="LogBuffer"/>.
/// </summary>
[ProviderAlias("LogStreamer")]
public sealed class LogStreamerLoggerProvider(LogBuffer buffer) : ILoggerProvider
{
    public ILogger CreateLogger(string categoryName)
        => new LogStreamerLogger(categoryName, buffer);

    public void Dispose() { }
}
