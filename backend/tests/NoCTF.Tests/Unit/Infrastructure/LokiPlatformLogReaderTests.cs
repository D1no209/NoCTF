using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using NoCTF.Application.Administration.PlatformLogs;
using NoCTF.Infrastructure.Observability;

namespace NoCTF.Tests.Unit.Infrastructure;

public sealed class LokiPlatformLogReaderTests
{
    [Test]
    public async Task Queries_filter_page_and_export_redacted_logs(CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        var competitionId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var warning = View(now, PlatformLogLevel.Warning, competitionId,
            "Failed operation with [REDACTED]") with { UserId = userId };
        var information = View(now.AddSeconds(-1), PlatformLogLevel.Information,
            competitionId, "Runner initialized.");
        var body = JsonSerializer.Serialize(new
        {
            status = "success",
            data = new
            {
                resultType = "streams",
                result = new[]
                {
                    new
                    {
                        stream = new { service_name = "noctf-host-runner" },
                        values = new[]
                        {
                            new[] { Nanoseconds(warning.Timestamp), Serialize(warning) },
                            new[] { Nanoseconds(information.Timestamp), Serialize(information) }
                        }
                    }
                }
            }
        });
        await Assert.That(body).DoesNotContain(userId.ToString());
        using var client = new HttpClient(new StubHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            })) { BaseAddress = new Uri("http://loki.test/") };
        var reader = new LokiPlatformLogReader(client, TimeProvider.System,
            NewProtector());
        var first = await reader.QueryAsync(Query(
            PlatformLogLevel.Warning, competitionId, null, 1) with
        {
            UserId = userId
        }, ct);
        await Assert.That(first.State).IsEqualTo(PlatformLogReadState.Available);
        await Assert.That(first.Items).Count().IsEqualTo(1);
        await Assert.That(first.Items[0]).IsEqualTo(warning);
        await Assert.That(first.NextCursor).IsEqualTo(warning.Cursor);

        var second = await reader.QueryAsync(Query(
            PlatformLogLevel.Information, competitionId, first.NextCursor, 10), ct);
        await Assert.That(second.Items).Count().IsEqualTo(1);
        await Assert.That(second.Items[0]).IsEqualTo(information);

        var export = await reader.ExportAsync(Query(
            PlatformLogLevel.Warning, competitionId, null, 50_000) with
        {
            From = now.AddMinutes(-1), To = now.AddMinutes(1)
        }, ct);
        await Assert.That(export.State).IsEqualTo(PlatformLogReadState.Available);
        await using var stream = export.Export!.Content;
        using var text = new StreamReader(stream);
        var jsonl = await text.ReadToEndAsync(ct);
        await Assert.That(jsonl).Contains("[REDACTED]");
        await Assert.That(jsonl).DoesNotContain("do-not-export-flag");
    }

    [Test]
    public async Task Loki_failure_is_reported_as_unavailable(CancellationToken ct)
    {
        using var client = new HttpClient(new StubHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)))
        { BaseAddress = new Uri("http://loki.test/") };
        var reader = new LokiPlatformLogReader(client, TimeProvider.System,
            NewProtector());
        var result = await reader.QueryAsync(Query(
            PlatformLogLevel.Warning, null, null, 10), ct);
        await Assert.That(result.State).IsEqualTo(PlatformLogReadState.Unavailable);
    }

    private static PlatformLogView View(DateTimeOffset at, PlatformLogLevel level,
        Guid competitionId, string message) => new(
        at.UtcTicks + ":" + Guid.NewGuid().ToString("N"), at,
        PlatformLogService.Runner, level, "NoCTF.Runner.Messages.RuntimeHandlers",
        7, "RuntimeEvent", message, null, null, competitionId,
        null, null, null, null, null);

    private static PlatformLogQuery Query(PlatformLogLevel level,
        Guid? competitionId, string? cursor, int limit) => new(
        level, PlatformLogService.Runner, null, null, null, null,
        competitionId, null, null, null, null, null, cursor, limit);

    private static string Nanoseconds(DateTimeOffset at) =>
        checked((at.UtcTicks - DateTime.UnixEpoch.Ticks) * 100)
            .ToString(System.Globalization.CultureInfo.InvariantCulture);

    private static string Serialize(PlatformLogView view) =>
        JsonSerializer.Serialize(new StoredPlatformLog(
            view with { UserId = null },
            view.UserId is Guid userId ? NewProtector().Protect(userId) : null),
            StoredPlatformLogJsonContext.Default.StoredPlatformLog);

    private static PlatformLogUserIdProtector NewProtector() => new(
        new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["RunnerScoring:SigningKey"] = new string('k', 64)
            }).Build());

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> response)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(response(request));
    }
}
