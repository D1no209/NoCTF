using System.Data.Common;
using Microsoft.Extensions.Logging;
using NoCTF.Infrastructure.Persistence;

namespace NoCTF.Tests.Unit.Infrastructure;

public sealed class TransactionConflictLoggerFactoryTests
{
    [Test]
    [Arguments("40001")]
    [Arguments("40P01")]
    public async Task Retryable_conflicts_in_ef_failure_events_are_logged_as_information(string sqlState)
    {
        var (factory, records) = Create();
        // Mirrors the execution strategy wrapping: InvalidOperationException -> DbUpdateException -> DbException.
        var exception = new InvalidOperationException("transient",
            new Exception("save", new TestDbException(sqlState)));

        foreach (var category in new[]
                 {
                     TransactionConflictLoggerFactory.UpdateCategory,
                     TransactionConflictLoggerFactory.CommandCategory,
                     TransactionConflictLoggerFactory.TransactionCategory
                 })
            factory.CreateLogger(category).Log(LogLevel.Error, new EventId(10000), "state", exception, (state, _) => state);

        await Assert.That(records.Select(record => record.Level).Distinct()).IsEquivalentTo([LogLevel.Information]);
        await Assert.That(records.Select(record => record.EventId).Distinct()).IsEquivalentTo([10000]);
        await Assert.That(records.All(record => ReferenceEquals(record.Exception, exception))).IsTrue();
    }

    [Test]
    public async Task Other_database_errors_keep_their_level()
    {
        var (factory, records) = Create();

        factory.CreateLogger(TransactionConflictLoggerFactory.CommandCategory)
            .Log(LogLevel.Error, new EventId(20102), "state", new TestDbException("23505"), (state, _) => state);
        factory.CreateLogger(TransactionConflictLoggerFactory.UpdateCategory)
            .Log(LogLevel.Error, new EventId(10000), "state", new InvalidOperationException("bug"), (state, _) => state);

        await Assert.That(records.Select(record => record.Level)).IsEquivalentTo([LogLevel.Error, LogLevel.Error]);
    }

    [Test]
    public async Task Unrelated_categories_are_not_wrapped()
    {
        var (factory, records) = Create();

        factory.CreateLogger("NoCTF.Worker.Something")
            .Log(LogLevel.Error, new EventId(1), "state", new TestDbException("40001"), (state, _) => state);

        await Assert.That(records.Single().Level).IsEqualTo(LogLevel.Error);
    }

    private static (TransactionConflictLoggerFactory Factory, List<Record> Records) Create()
    {
        var records = new List<Record>();
        var inner = LoggerFactory.Create(builder => builder
            .SetMinimumLevel(LogLevel.Trace)
            .AddProvider(new RecordingProvider(records)));
        return (new TransactionConflictLoggerFactory(inner), records);
    }

    private sealed record Record(string Category, LogLevel Level, int EventId, Exception? Exception);

    private sealed class RecordingProvider(List<Record> records) : ILoggerProvider
    {
        public ILogger CreateLogger(string categoryName) => new RecordingLogger(categoryName, records);
        public void Dispose() { }
    }

    private sealed class RecordingLogger(string category, List<Record> records) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) =>
            records.Add(new(category, logLevel, eventId.Id, exception));
    }

    private sealed class TestDbException(string sqlState) : DbException("database")
    {
        public override string SqlState { get; } = sqlState;
    }
}
