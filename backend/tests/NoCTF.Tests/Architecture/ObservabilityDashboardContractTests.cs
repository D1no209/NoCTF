using System.Text.Json;

namespace NoCTF.Tests.Architecture;

public sealed class ObservabilityDashboardContractTests
{
    private static readonly string RepositoryRoot = FindRepositoryRoot();

    [Test]
    public async Task Performance_dashboard_uses_available_metrics_and_stable_ranked_tables()
    {
        using var document = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(
            RepositoryRoot, "deploy", "observability", "grafana", "dashboards",
            "noctf-performance.json")));
        var root = document.RootElement;
        var panels = root.GetProperty("panels").EnumerateArray().ToArray();

        var cpu = Panel(panels, 19);
        await Assert.That(Query(cpu)).Contains(
            "dotnet_process_cpu_time_seconds_total{job=\"noctf\"}");
        await Assert.That(Query(cpu)).DoesNotContain(
            "process_cpu_seconds_total{job=\"noctf\"}");

        var targets = Query(Panel(panels, 23));
        await Assert.That(targets).Contains("blackbox").And.Contains("loki");

        foreach (var panelId in new[] { 8, 9, 25, 26, 54 })
        {
            var panel = Panel(panels, panelId);
            await Assert.That(panel.GetProperty("type").GetString()).IsEqualTo("table");
            var target = panel.GetProperty("targets")[0];
            await Assert.That(target.GetProperty("instant").GetBoolean()).IsTrue();
            await Assert.That(target.GetProperty("range").GetBoolean()).IsFalse();
        }

        foreach (var panelId in Enumerable.Range(47, 8))
            await Assert.That(panels.Any(panel =>
                panel.GetProperty("id").GetInt32() == panelId)).IsTrue();

        await Assert.That(root.GetProperty("links").EnumerateArray().Any(link =>
            link.GetProperty("url").GetString() == "/d/noctf-logs")).IsTrue();
    }

    [Test]
    public async Task Performance_alerts_use_consistent_ratios_and_minimum_samples()
    {
        var rules = await File.ReadAllTextAsync(Path.Combine(
            RepositoryRoot, "deploy", "observability", "prometheus", "rules",
            "recording-rules.yml"));
        var alerts = await File.ReadAllTextAsync(Path.Combine(
            RepositoryRoot, "deploy", "observability", "prometheus", "rules",
            "alerts.yml"));

        await Assert.That(rules).Contains(
            "clamp_min(sum(rate(noctf_api_requests_total{request_kind=\"rest\"}[5m])), 0.001)");
        await Assert.That(alerts).Contains(
            "noctf_api_request_duration_seconds_count{request_kind=\"rest\"}[5m])) >= 100");
        await Assert.That(alerts).Contains(
            "noctf_api_requests_total{request_kind=\"rest\"}[5m])) >= 100");
        await Assert.That(alerts).Contains(
            "noctf_nats_operation_duration_seconds_count[5m])) >= 20");
        await Assert.That(alerts).Contains(
            "noctf_webhook_queue_age_seconds_count[5m])) >= 20");
    }

    private static JsonElement Panel(JsonElement[] panels, int id) =>
        panels.Single(panel => panel.GetProperty("id").GetInt32() == id);

    private static string Query(JsonElement panel) =>
        panel.GetProperty("targets")[0].GetProperty("expr").GetString()!;

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "AGENTS.md"))
                && Directory.Exists(Path.Combine(directory.FullName, "backend")))
                return directory.FullName;
        }

        throw new DirectoryNotFoundException("NoCTF repository root was not found.");
    }
}
