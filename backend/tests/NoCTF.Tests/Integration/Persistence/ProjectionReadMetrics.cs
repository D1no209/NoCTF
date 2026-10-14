using System.Collections;
using System.Data;
using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace NoCTF.Tests.Integration.Persistence;

/// <summary>Measures the rows actually consumed by projection queries, without recording parameter values or row contents.</summary>
internal sealed class ProjectionReadMetrics : DbCommandInterceptor
{
    public List<ProjectionReadCommand> Commands { get; } = [];
    public void Reset() => Commands.Clear();

    public override DbDataReader ReaderExecuted(DbCommand command, CommandExecutedEventData eventData, DbDataReader result) =>
        Observe(command, result);

    public override ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand command, CommandExecutedEventData eventData,
        DbDataReader result, CancellationToken cancellationToken = default) => ValueTask.FromResult(Observe(command, result));

    private DbDataReader Observe(DbCommand command, DbDataReader reader)
    {
        // Npgsql's modification batch consumes provider-specific reader metadata for
        // INSERT/UPDATE/DELETE ... RETURNING. Preserve its native reader on write paths.
        if (!command.CommandText.TrimStart().StartsWith("SELECT", StringComparison.OrdinalIgnoreCase))
            return reader;
        var measurement = new ProjectionReadCommand(command.CommandText, reader.FieldCount);
        Commands.Add(measurement);
        return new CountingReader(reader, measurement);
    }

    private sealed class CountingReader(DbDataReader reader, ProjectionReadCommand measurement) : DbDataReader
    {
        public override bool Read()
        {
            var found = reader.Read();
            if (found) measurement.AddRow();
            return found;
        }

        public override async Task<bool> ReadAsync(CancellationToken cancellationToken)
        {
            var found = await reader.ReadAsync(cancellationToken);
            if (found) measurement.AddRow();
            return found;
        }

        public override object this[int ordinal] => reader[ordinal];
        public override object this[string name] => reader[name];
        public override int Depth => reader.Depth;
        public override int FieldCount => reader.FieldCount;
        public override int VisibleFieldCount => reader.VisibleFieldCount;
        public override bool HasRows => reader.HasRows;
        public override bool IsClosed => reader.IsClosed;
        public override int RecordsAffected => reader.RecordsAffected;
        public override bool NextResult() => reader.NextResult();
        public override Task<bool> NextResultAsync(CancellationToken cancellationToken) => reader.NextResultAsync(cancellationToken);
        public override bool GetBoolean(int ordinal) => reader.GetBoolean(ordinal);
        public override byte GetByte(int ordinal) => reader.GetByte(ordinal);
        public override long GetBytes(int ordinal, long dataOffset, byte[]? buffer, int bufferOffset, int length) =>
            reader.GetBytes(ordinal, dataOffset, buffer, bufferOffset, length);
        public override char GetChar(int ordinal) => reader.GetChar(ordinal);
        public override long GetChars(int ordinal, long dataOffset, char[]? buffer, int bufferOffset, int length) =>
            reader.GetChars(ordinal, dataOffset, buffer, bufferOffset, length);
        public override string GetDataTypeName(int ordinal) => reader.GetDataTypeName(ordinal);
        public override DateTime GetDateTime(int ordinal) => reader.GetDateTime(ordinal);
        public override decimal GetDecimal(int ordinal) => reader.GetDecimal(ordinal);
        public override double GetDouble(int ordinal) => reader.GetDouble(ordinal);
        public override IEnumerator GetEnumerator() => ((IEnumerable)reader).GetEnumerator();
        public override Type GetFieldType(int ordinal) => reader.GetFieldType(ordinal);
        public override T GetFieldValue<T>(int ordinal) => reader.GetFieldValue<T>(ordinal);
        public override Task<T> GetFieldValueAsync<T>(int ordinal, CancellationToken cancellationToken) =>
            reader.GetFieldValueAsync<T>(ordinal, cancellationToken);
        public override float GetFloat(int ordinal) => reader.GetFloat(ordinal);
        public override Guid GetGuid(int ordinal) => reader.GetGuid(ordinal);
        public override short GetInt16(int ordinal) => reader.GetInt16(ordinal);
        public override int GetInt32(int ordinal) => reader.GetInt32(ordinal);
        public override long GetInt64(int ordinal) => reader.GetInt64(ordinal);
        public override string GetName(int ordinal) => reader.GetName(ordinal);
        public override int GetOrdinal(string name) => reader.GetOrdinal(name);
        public override string GetString(int ordinal) => reader.GetString(ordinal);
        public override object GetValue(int ordinal) => reader.GetValue(ordinal);
        public override int GetValues(object[] values) => reader.GetValues(values);
        public override bool IsDBNull(int ordinal) => reader.IsDBNull(ordinal);
        public override Task<bool> IsDBNullAsync(int ordinal, CancellationToken cancellationToken) => reader.IsDBNullAsync(ordinal, cancellationToken);
        public override DataTable? GetSchemaTable() => reader.GetSchemaTable();
        public override Stream GetStream(int ordinal) => reader.GetStream(ordinal);
        public override TextReader GetTextReader(int ordinal) => reader.GetTextReader(ordinal);
        public override void Close() => reader.Close();
        public override Task CloseAsync() => reader.CloseAsync();
        protected override void Dispose(bool disposing)
        {
            if (disposing) reader.Dispose();
            base.Dispose(disposing);
        }
        public override ValueTask DisposeAsync() => reader.DisposeAsync();
    }
}

internal sealed class ProjectionReadCommand(string sql, int columns)
{
    public string Sql { get; } = sql;
    public int Columns { get; } = columns;
    public long Rows { get; private set; }
    internal void AddRow() => Rows++;
}
