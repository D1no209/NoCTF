using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;
using NoCTF.Application.Observability;

namespace NoCTF.Infrastructure.Observability;

public sealed class DatabaseCommandPerformanceInterceptor : DbCommandInterceptor
{
    private static void Record(CommandExecutedEventData data) => NoCtfTelemetry.RecordDatabaseOperation(
        data.CommandSource == CommandSource.SaveChanges ? DatabasePerformanceStage.Write : DatabasePerformanceStage.Read,
        data.Duration.TotalSeconds);

    public override DbDataReader ReaderExecuted(DbCommand command, CommandExecutedEventData eventData, DbDataReader result)
    { Record(eventData); return result; }
    public override ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand command, CommandExecutedEventData eventData,
        DbDataReader result, CancellationToken cancellationToken = default)
    { Record(eventData); return ValueTask.FromResult(result); }
    public override int NonQueryExecuted(DbCommand command, CommandExecutedEventData eventData, int result)
    { Record(eventData); return result; }
    public override ValueTask<int> NonQueryExecutedAsync(DbCommand command, CommandExecutedEventData eventData,
        int result, CancellationToken cancellationToken = default)
    { Record(eventData); return ValueTask.FromResult(result); }
    public override object? ScalarExecuted(DbCommand command, CommandExecutedEventData eventData, object? result)
    { Record(eventData); return result; }
    public override ValueTask<object?> ScalarExecutedAsync(DbCommand command, CommandExecutedEventData eventData,
        object? result, CancellationToken cancellationToken = default)
    { Record(eventData); return ValueTask.FromResult(result); }
    public override void CommandFailed(DbCommand command, CommandErrorEventData eventData) =>
        NoCtfTelemetry.RecordDatabaseOperation(DatabasePerformanceStage.Failed, eventData.Duration.TotalSeconds);
    public override Task CommandFailedAsync(DbCommand command, CommandErrorEventData eventData, CancellationToken cancellationToken = default)
    { CommandFailed(command, eventData); return Task.CompletedTask; }
}

public sealed class DatabaseConnectionPerformanceInterceptor : DbConnectionInterceptor
{
    public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData) =>
        NoCtfTelemetry.RecordDatabaseOperation(DatabasePerformanceStage.ConnectionOpen, eventData.Duration.TotalSeconds);
    public override Task ConnectionOpenedAsync(DbConnection connection, ConnectionEndEventData eventData, CancellationToken cancellationToken = default)
    { ConnectionOpened(connection, eventData); return Task.CompletedTask; }
}
