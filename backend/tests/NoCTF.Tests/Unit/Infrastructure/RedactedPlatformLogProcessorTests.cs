using System.Text.Json;
using System.Diagnostics;
using System.Data.Common;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using NoCTF.Application.Administration.PlatformLogs;
using NoCTF.Hosting.Observability;
using NoCTF.Infrastructure.Observability;
using OpenTelemetry;
using OpenTelemetry.Logs;

namespace NoCTF.Tests.Unit.Infrastructure;

public sealed class RedactedPlatformLogProcessorTests
{
    [Test]
    public async Task Framework_request_failures_are_classified_as_api_in_a_combined_host()
    {
        var exporter = new CaptureExporter();
        using var factory = LoggerFactory.Create(builder => builder.AddOpenTelemetry(options =>
        {
            options.IncludeFormattedMessage = true;
            options.AddProcessor(new RedactedPlatformLogProcessor(
                new PlatformLogBroadcastQueue(), PlatformLogService.Host, NewProtector()));
            options.AddProcessor(new SimpleLogRecordExportProcessor(exporter));
        }));
        factory.CreateLogger("Microsoft.AspNetCore.Diagnostics.ExceptionHandlerMiddleware")
            .LogError(new EventId(1), "Unhandled request failure.");
        var stored = JsonSerializer.Deserialize(exporter.Body!, StoredPlatformLogJsonContext.Default.StoredPlatformLog);
        await Assert.That(stored!.View.Service).IsEqualTo(PlatformLogService.Api);
    }

    [Test]
    public async Task Multiple_collection_warning_includes_only_the_bounded_query_source()
    {
        var queue = new PlatformLogBroadcastQueue();
        var exporter = new CaptureExporter();
        using var factory = LoggerFactory.Create(builder => builder.AddOpenTelemetry(options =>
        {
            options.IncludeFormattedMessage = true;
            options.AddProcessor(new RedactedPlatformLogProcessor(
                queue, PlatformLogService.Api, NewProtector()));
            options.AddProcessor(new SimpleLogRecordExportProcessor(exporter));
        }));
        using var activity = new Activity("request").Start();
        activity.SetTag("noctf.endpoint", "/api/v1/competitions/{competitionId}");

        factory.CreateLogger("Microsoft.EntityFrameworkCore.Query").LogWarning(
            new EventId(20504, "MultipleCollectionIncludeWarning"),
            "Compiling a query with multiple collections.");

        var stored = JsonSerializer.Deserialize(exporter.Body!,
            StoredPlatformLogJsonContext.Default.StoredPlatformLog);
        await Assert.That(stored!.View.Message)
            .Contains("Operation source: /api/v1/competitions/{competitionId}.");
    }

    [Test]
    public async Task Framework_database_command_is_exported_without_sql_or_parameters()
    {
        var queue = new PlatformLogBroadcastQueue();
        var exporter = new CaptureExporter();
        using var factory = LoggerFactory.Create(builder => builder.AddOpenTelemetry(options =>
        {
            options.IncludeFormattedMessage = true;
            options.ParseStateValues = true;
            options.AddProcessor(new RedactedPlatformLogProcessor(
                queue, PlatformLogService.Host, NewProtector()));
            options.AddProcessor(new SimpleLogRecordExportProcessor(exporter));
        }));
        factory.CreateLogger("Microsoft.EntityFrameworkCore.Database.Command")
            .LogError("Failed executing SELECT secret_column FROM users WHERE id = {UserId}.",
                Guid.NewGuid());

        await Assert.That(exporter.Body).IsNotNull();
        await Assert.That(exporter.Body!).DoesNotContain("SELECT");
        await Assert.That(exporter.Body!).DoesNotContain("secret_column");
        await Assert.That(exporter.Body!).DoesNotContain("users");
        var stored = JsonSerializer.Deserialize(exporter.Body!,
            StoredPlatformLogJsonContext.Default.StoredPlatformLog);
        await Assert.That(stored!.View.Category)
            .IsEqualTo("Microsoft.EntityFrameworkCore.Database.Command");
        await Assert.That(stored.View.Message)
            .StartsWith("Database command details were suppressed before log export.");
    }

    [Test]
    [Arguments("Microsoft.EntityFrameworkCore.Database.Command", 20102)]
    [Arguments("Microsoft.EntityFrameworkCore.Update", 10000)]
    [Arguments("Microsoft.AspNetCore.Diagnostics.ExceptionHandlerMiddleware", 1)]
    public async Task Failure_logs_include_only_the_bounded_diagnostic_source(
        string category, int eventId)
    {
        var queue = new PlatformLogBroadcastQueue();
        var exporter = new CaptureExporter();
        using var factory = LoggerFactory.Create(builder => builder.AddOpenTelemetry(options =>
        {
            options.IncludeFormattedMessage = true;
            options.AddProcessor(new RedactedPlatformLogProcessor(
                queue, PlatformLogService.Api, NewProtector()));
            options.AddProcessor(new SimpleLogRecordExportProcessor(exporter));
        }));
        using var activity = new Activity("request").Start();
        activity.SetTag("noctf.endpoint", "/api/v1/competitions/{competitionId}/challenges");

        factory.CreateLogger(category).LogError(
            new EventId(eventId), "Failed executing SELECT secret_column FROM users.");

        var stored = JsonSerializer.Deserialize(exporter.Body!,
            StoredPlatformLogJsonContext.Default.StoredPlatformLog);
        await Assert.That(stored!.View.Message)
            .Contains("Operation source: /api/v1/competitions/{competitionId}/challenges.");
        await Assert.That(exporter.Body!).DoesNotContain("SELECT secret_column");
    }

    [Test]
    public async Task Database_update_log_exports_only_safe_sql_state_and_source()
    {
        var queue = new PlatformLogBroadcastQueue();
        var exporter = new CaptureExporter();
        using var factory = LoggerFactory.Create(builder => builder.AddOpenTelemetry(options =>
        {
            options.IncludeFormattedMessage = true;
            options.AddProcessor(new RedactedPlatformLogProcessor(
                queue, PlatformLogService.Worker, NewProtector()));
            options.AddProcessor(new SimpleLogRecordExportProcessor(exporter));
        }));
        using var activity = new Activity("message").Start();
        activity.SetTag("messaging.message.type", "NoCTF.Worker.DispatchRuntime");
        var exception = new InvalidOperationException("SELECT secret FROM accounts",
            new TestDatabaseException("40001", "password=super-secret"));

        factory.CreateLogger("Microsoft.EntityFrameworkCore.Update").LogError(
            new EventId(10000), exception, "Failed: SELECT secret FROM accounts");

        var stored = JsonSerializer.Deserialize(exporter.Body!,
            StoredPlatformLogJsonContext.Default.StoredPlatformLog);
        await Assert.That(stored!.View.Message).Contains("SQLSTATE: 40001.");
        await Assert.That(stored.View.Message)
            .Contains("Operation source: NoCTF.Worker.DispatchRuntime.");
        await Assert.That(exporter.Body!).DoesNotContain("SELECT secret");
        await Assert.That(exporter.Body!).DoesNotContain("super-secret");
        await Assert.That(stored.View.ExceptionType)
            .IsEqualTo(typeof(InvalidOperationException).FullName);
    }

    [Test]
    public async Task Exception_logs_export_safe_chain_objects_frames_source_and_trace()
    {
        var exporter = new CaptureExporter();
        using var factory = LoggerFactory.Create(builder => builder.AddOpenTelemetry(options =>
        {
            options.IncludeFormattedMessage = true;
            options.AddProcessor(new RedactedPlatformLogProcessor(
                new PlatformLogBroadcastQueue(), PlatformLogService.Worker, NewProtector()));
            options.AddProcessor(new SimpleLogRecordExportProcessor(exporter));
        }));
        using var activity = new Activity("message").SetIdFormat(ActivityIdFormat.W3C).Start();
        activity.SetTag("messaging.message.type", "NoCTF.Worker.DeliverStaffWebhook");
        var database = new TestDatabaseException("23505", "duplicate key value (secret-value)");
        database.Data["TableName"] = "competition_staff_webhook_deliveries";
        database.Data["ConstraintName"] = "pk_competition_staff_webhook_deliveries";
        database.Data["Detail"] = "Key (event_id)=(secret-value) already exists.";
        database.Data["MessageText"] = "duplicate key value (secret-value)";
        database.Data["ColumnName"] = "x'; DROP TABLE users; --";
        var exception = await ThrowFromApplicationAsync(database);

        factory.CreateLogger("NoCTF.Worker.StaffWebhooks").LogError(exception, "Delivery failed.");

        var stored = JsonSerializer.Deserialize(exporter.Body!,
            StoredPlatformLogJsonContext.Default.StoredPlatformLog);
        var message = stored!.View.Message;
        await Assert.That(message).Contains("SQLSTATE: 23505.");
        await Assert.That(message).Contains(
            "Database objects: TableName=competition_staff_webhook_deliveries, " +
            "ConstraintName=pk_competition_staff_webhook_deliveries.");
        await Assert.That(message).Contains("Operation source: NoCTF.Worker.DeliverStaffWebhook.");
        await Assert.That(message).Contains(
            $"Exception chain: {typeof(InvalidOperationException).FullName} -> " +
            $"{typeof(TestDatabaseException).FullName}.");
        await Assert.That(message).Contains(
            $"{typeof(RedactedPlatformLogProcessorTests).FullName}.{nameof(ThrowFromApplicationAsync)}");
        await Assert.That(message).Contains($"Trace: {activity.TraceId.ToHexString()}.");
        await Assert.That(exporter.Body!).DoesNotContain("secret-value");
        await Assert.That(exporter.Body!).DoesNotContain("DROP TABLE");
        await Assert.That(stored.View.ExceptionMessage).IsNull();
    }

    [Test]
    public async Task Transaction_error_includes_operation_source()
    {
        var exporter = new CaptureExporter();
        using var factory = LoggerFactory.Create(builder => builder.AddOpenTelemetry(options =>
        {
            options.IncludeFormattedMessage = true;
            options.AddProcessor(new RedactedPlatformLogProcessor(
                new PlatformLogBroadcastQueue(), PlatformLogService.Worker, NewProtector()));
            options.AddProcessor(new SimpleLogRecordExportProcessor(exporter));
        }));
        using var activity = new Activity("message").Start();
        activity.SetTag("messaging.message.type", "NoCTF.Worker.RecomputeLeaderboard");

        factory.CreateLogger("Microsoft.EntityFrameworkCore.Database.Transaction").LogError(
            new EventId(20205), "An error occurred using a transaction.");

        var stored = JsonSerializer.Deserialize(exporter.Body!,
            StoredPlatformLogJsonContext.Default.StoredPlatformLog);
        await Assert.That(stored!.View.Message)
            .Contains("Operation source: NoCTF.Worker.RecomputeLeaderboard.");
    }

    private static async Task<Exception> ThrowFromApplicationAsync(Exception inner)
    {
        try
        {
            await Task.Yield();
            throw new InvalidOperationException("SELECT secret-value FROM accounts", inner);
        }
        catch (Exception exception)
        {
            return exception;
        }
    }

    [Test]
    public async Task Exception_text_and_sql_are_not_exported()
    {
        var queue = new PlatformLogBroadcastQueue();
        var exporter = new CaptureExporter();
        using var factory = LoggerFactory.Create(builder => builder.AddOpenTelemetry(options =>
        {
            options.IncludeFormattedMessage = true;
            options.AddProcessor(new RedactedPlatformLogProcessor(
                queue, PlatformLogService.Api, NewProtector()));
            options.AddProcessor(new SimpleLogRecordExportProcessor(exporter));
        }));
        factory.CreateLogger("NoCTF.API.Security.Admission").LogError(
            new InvalidOperationException("SELECT secret_parameter FROM users"),
            "A database operation failed.");

        await Assert.That(exporter.Body).IsNotNull();
        await Assert.That(exporter.Body!).DoesNotContain("SELECT secret_parameter");
        var stored = JsonSerializer.Deserialize(exporter.Body!,
            StoredPlatformLogJsonContext.Default.StoredPlatformLog);
        await Assert.That(stored!.View.ExceptionType)
            .IsEqualTo(typeof(InvalidOperationException).FullName);
        await Assert.That(stored.View.ExceptionMessage).IsNull();
    }

    [Test]
    public async Task Otlp_record_and_live_notification_contain_only_redacted_content()
    {
        var queue = new PlatformLogBroadcastQueue();
        var exporter = new CaptureExporter();
        using var factory = LoggerFactory.Create(builder => builder.AddOpenTelemetry(options =>
        {
            options.IncludeFormattedMessage = true;
            options.ParseStateValues = true;
            options.AddProcessor(new RedactedPlatformLogProcessor(
                queue, PlatformLogService.Api, NewProtector()));
            options.AddProcessor(new SimpleLogRecordExportProcessor(exporter));
        }));
        var logger = factory.CreateLogger("NoCTF.API.Security.Admission");
        var competitionId = Guid.NewGuid();
        const string flag = "flag{do-not-export-this}";
        const string password = "do-not-export-password";
        logger.LogWarning("Competition {CompetitionId} received Flag {Flag} and Password {Password}.",
            competitionId, flag, password);

        await Assert.That(exporter.Body).IsNotNull();
        await Assert.That(exporter.Body!).DoesNotContain(flag);
        await Assert.That(exporter.Body!).DoesNotContain(password);
        var stored = JsonSerializer.Deserialize(exporter.Body!,
            StoredPlatformLogJsonContext.Default.StoredPlatformLog);
        await Assert.That(stored!.View.CompetitionId).IsEqualTo(competitionId);
        await Assert.That(stored.View.Message).Contains("[REDACTED]");
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(1));
        await foreach (var live in queue.ReadAllAsync(timeout.Token))
        {
            await Assert.That(live.Message).DoesNotContain(flag);
            await Assert.That(live.Message).DoesNotContain(password);
            break;
        }
    }

    private static PlatformLogUserIdProtector NewProtector() => new(
        new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["RunnerScoring:SigningKey"] = new string('k', 64)
            }).Build());

    private sealed class CaptureExporter : BaseExporter<LogRecord>
    {
        public string? Body { get; private set; }

        public override ExportResult Export(in Batch<LogRecord> batch)
        {
            foreach (var record in batch)
                Body = record.Body;
            return ExportResult.Success;
        }
    }

    private sealed class TestDatabaseException(string state, string message)
        : DbException(message)
    {
        public override string? SqlState => state;
    }
}
