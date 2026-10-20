using System.Data.Common;
using Microsoft.Extensions.Logging;

namespace NoCTF.Infrastructure.Persistence;

/// <summary>
/// Logger factory used by EF Core. Serialization failures and deadlocks (SQLSTATE 40001/40P01) are the
/// expected outcome of serializable transactions and are retried by the owning store; EF reports every
/// attempt at Error before the store decides. Those three EF failure events are logged at Information so
/// a retried conflict does not look like a production fault. Exhausted retries still surface through the
/// caller (HTTP 503 TransactionBusy with a warning, or a failed message handler). All other database
/// errors keep their original level.
/// </summary>
public sealed class TransactionConflictLoggerFactory(ILoggerFactory inner) : ILoggerFactory
{
    internal const string UpdateCategory = "Microsoft.EntityFrameworkCore.Update";
    internal const string CommandCategory = "Microsoft.EntityFrameworkCore.Database.Command";
    internal const string TransactionCategory = "Microsoft.EntityFrameworkCore.Database.Transaction";

    public ILogger CreateLogger(string categoryName)
    {
        var logger = inner.CreateLogger(categoryName);
        return categoryName is UpdateCategory or CommandCategory or TransactionCategory
            ? new ConflictAwareLogger(logger)
            : logger;
    }

    public void AddProvider(ILoggerProvider provider) => inner.AddProvider(provider);

    // The application host owns the inner factory.
    public void Dispose() { }

    internal static bool IsRetryableConflict(Exception? exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
            if (current is DbException { SqlState: "40001" or "40P01" })
                return true;
        return false;
    }

    private sealed class ConflictAwareLogger(ILogger inner) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => inner.BeginScope(state);

        // EF checks the definition level before building event data, so the original level must stay enabled.
        public bool IsEnabled(LogLevel logLevel) => inner.IsEnabled(logLevel);

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (logLevel > LogLevel.Information && IsRetryableConflict(exception))
                logLevel = LogLevel.Information;
            if (inner.IsEnabled(logLevel))
                inner.Log(logLevel, eventId, state, exception, formatter);
        }
    }
}
