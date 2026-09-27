using System.Text.Json;
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
            .IsEqualTo("Database command details were suppressed before log export.");
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
}
