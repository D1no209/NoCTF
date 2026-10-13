using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Configurations;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using NoCTF.Application.Administration.PlatformLogs;
using NoCTF.Hosting.Observability;
using NoCTF.Infrastructure.Observability;
using OpenTelemetry;
using OpenTelemetry.Exporter;
using OpenTelemetry.Logs;
using OpenTelemetry.Resources;

namespace NoCTF.Tests.Integration.Administration;

[Category("Integration"), NotInParallel]
public sealed class LokiOtlpIntegrationTests
{
    [Test, Timeout(180_000)]
    public async Task Redacted_otlp_log_can_be_queried_from_private_loki(CancellationToken ct)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            var config = FindConfig();
            await using var loki = new ContainerBuilder(
                    "grafana/loki:3.6.0@sha256:6a705de65df88aa0d90a44779606b0042722c86637335a73858f65f9fe9f9557")
                .WithBindMount(config, "/etc/loki/loki.yml", AccessMode.ReadOnly)
                .WithCreateParameterModifier(parameters => parameters.User = "0")
                .WithPortBinding(3100, assignRandomHostPort: true)
                .WithCommand("-config.file=/etc/loki/loki.yml")
                .WithWaitStrategy(Wait.ForUnixContainer().UntilHttpRequestIsSucceeded(
                    request => request.ForPort(3100).ForPath("/ready")))
                .Build();
            await loki.StartAsync(ct);
            var baseUrl = $"http://{loki.Hostname}:{loki.GetMappedPublicPort(3100)}/";
            var userIds = NewProtector();
            using var loggerFactory = LoggerFactory.Create(builder => builder.AddOpenTelemetry(options =>
            {
                options.SetResourceBuilder(ResourceBuilder.CreateDefault()
                    .AddService("noctf-host-api"));
                options.IncludeFormattedMessage = true;
                options.ParseStateValues = true;
                options.AddProcessor(new RedactedPlatformLogProcessor(
                    new PlatformLogBroadcastQueue(), PlatformLogService.Api, userIds));
                options.AddOtlpExporter((exporter, processor) =>
                {
                    exporter.Endpoint = new Uri(baseUrl + "otlp/v1/logs");
                    exporter.Protocol = OtlpExportProtocol.HttpProtobuf;
                    processor.ExportProcessorType = ExportProcessorType.Simple;
                });
            }));
            var id = Guid.NewGuid();
            var userId = Guid.NewGuid();
            loggerFactory.CreateLogger("NoCTF.API.Integration")
                .LogError("Competition {CompetitionId} user {UserId} rejected Flag {Flag}.",
                    id, userId, "flag{must-not-reach-loki}");
            using var client = new HttpClient { BaseAddress = new Uri(baseUrl) };
            var reader = new LokiPlatformLogReader(client, TimeProvider.System,
                userIds);
            PlatformLogQueryResult? result = null;
            for (var attempt = 0; attempt < 20; attempt++)
            {
                result = await reader.QueryAsync(new(
                    PlatformLogLevel.Warning, PlatformLogService.Api,
                    DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddMinutes(1),
                    null, null, id, null, null, userId, null, null, null, 20), ct);
                if (result.Items.Count > 0) break;
                await Task.Delay(250, ct);
            }
            await Assert.That(result!.State).IsEqualTo(PlatformLogReadState.Available);
            await Assert.That(result.Items).Count().IsEqualTo(1);
            await Assert.That(result.Items[0].CompetitionId).IsEqualTo(id);
            await Assert.That(result.Items[0].UserId).IsEqualTo(userId);
            await Assert.That(result.Items[0].Message)
                .DoesNotContain("flag{must-not-reach-loki}");
            const string errorsQuery =
                "sum(count_over_time({service_name=~\"noctf-host-.*\"} | json | view_level=~\"4|5\" [5m]))";
            using var errors = await client.GetAsync(
                "loki/api/v1/query?query=" + Uri.EscapeDataString(errorsQuery), ct);
            await Assert.That(errors.IsSuccessStatusCode).IsTrue();
            using var dashboardResult = JsonDocument.Parse(
                await errors.Content.ReadAsStringAsync(ct));
            await Assert.That(dashboardResult.RootElement.GetProperty("data")
                .GetProperty("result").GetArrayLength()).IsGreaterThan(0);
        });
    }

    private static PlatformLogUserIdProtector NewProtector() => new(
        new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["RunnerScoring:SigningKey"] = new string('k', 64)
            }).Build());

    private static string FindConfig()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
             directory is not null; directory = directory.Parent)
        {
            var path = Path.Combine(directory.FullName, "deploy", "shared", "observability",
                "loki", "loki.yml");
            if (File.Exists(path)) return path;
        }
        throw new FileNotFoundException("Repository Loki configuration was not found.");
    }
}
