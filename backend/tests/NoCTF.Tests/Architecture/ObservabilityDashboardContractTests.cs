using System.Text.Json;

namespace NoCTF.Tests.Architecture;

public sealed class ObservabilityDashboardContractTests
{
    private static readonly string RepositoryRoot = FindRepositoryRoot();

    [Test]
    public async Task Runtime_mutation_endpoints_declare_typed_metrics_metadata()
    {
        var endpointRoot = Path.Combine(
            RepositoryRoot, "backend", "src", "NoCTF.API", "Endpoints");
        foreach (var path in new[]
                 {
                     "Runtime/CreateRuntimeEndpoint.cs",
                     "Runtime/ExtendRuntimeEndpoint.cs",
                     "Runtime/StopRuntimeEndpoint.cs",
                     "Administration/Runtime/CreateSharedRuntimeEndpoint.cs",
                     "Administration/Runtime/CreateTeamRuntimeEndpoint.cs",
                     "Administration/Runtime/StopSharedRuntimeEndpoint.cs",
                     "Administration/Runtime/StopTeamRuntimeEndpoint.cs",
                     "Administration/Runtime/ExtendTeamRuntimeEndpoint.cs",
                     "Administration/Runtime/TerminateRuntimeEndpoint.cs",
                     "Administration/Runtime/CreateForceTerminationEndpoint.cs",
                     "Administration/ChallengeBank/CreateChallengeTestRuntimeEndpoint.cs",
                     "Administration/ChallengeBank/ExtendChallengeTestRuntimeEndpoint.cs",
                     "Administration/ChallengeBank/StopChallengeTestRuntimeEndpoint.cs"
                 })
        {
            var source = await File.ReadAllTextAsync(Path.Combine(
                endpointRoot,
                path.Replace('/', Path.DirectorySeparatorChar)));
            await Assert.That(source).Contains("RuntimeOperationMetricsMetadata");
        }
    }

    [Test]
    public async Task Performance_dashboard_uses_available_metrics_and_stable_ranked_tables()
    {
        using var document = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(
            RepositoryRoot, "deploy", "shared", "observability", "grafana", "dashboards",
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

        foreach (var panelId in Enumerable.Range(47, 19))
            await Assert.That(panels.Any(panel =>
                panel.GetProperty("id").GetInt32() == panelId)).IsTrue();

        await Assert.That(Query(Panel(panels, 57)))
            .Contains("noctf_runner_capacity_transaction_retries_total");
        await Assert.That(Query(Panel(panels, 58)))
            .Contains("noctf_runtime_mutation_failures_total");
        await Assert.That(Query(Panel(panels, 60)))
            .Contains("noctf_webhook_materialization_races_total");
        await Assert.That(Query(Panel(panels, 8))).Contains(">= 20");
        await Assert.That(Query(Panel(panels, 63)))
            .Contains("noctf_webhook_recovery_scans_total");
        await Assert.That(Query(Panel(panels, 64)))
            .Contains("noctf_api_requests_total");
        await Assert.That(Query(Panel(panels, 65)))
            .Contains("noctf_webhook_recovery_scan_duration_seconds_bucket");

        await Assert.That(root.GetProperty("links").EnumerateArray().Any(link =>
            link.GetProperty("url").GetString() == "/d/noctf-logs")).IsTrue();
        await Assert.That(root.GetProperty("links").EnumerateArray().Any(link =>
            link.GetProperty("url").GetString() == "/d/noctf-cap")).IsTrue();
    }

    [Test]
    public async Task Cap_dashboard_separates_provider_latency_from_browser_solve_time()
    {
        using var document = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(
            RepositoryRoot, "deploy", "shared", "observability", "grafana", "dashboards",
            "noctf-cap.json")));
        var panels = document.RootElement.GetProperty("panels").EnumerateArray().ToArray();

        await Assert.That(Query(Panel(panels, 7)))
            .Contains("noctf_cap_siteverify_duration_seconds_bucket");
        await Assert.That(Query(Panel(panels, 9)))
            .Contains("noctf_cap_verified_today");
        await Assert.That(Query(Panel(panels, 10)))
            .Contains("noctf_cap_average_solve_duration_seconds");
        await Assert.That(Query(Panel(panels, 11)))
            .Contains("probe_duration_seconds");
        await Assert.That(Query(Panel(panels, 12)))
            .Contains("redis_memory_used_bytes{job=\"cap-valkey\"}");
        await Assert.That(Query(Panel(panels, 15)))
            .Contains("noctf_cap_siteverify_failures_total");

        var dashboard = await File.ReadAllTextAsync(Path.Combine(
            RepositoryRoot, "deploy", "shared", "observability", "grafana", "dashboards",
            "noctf-cap.json"));
        await Assert.That(dashboard).DoesNotContain("siteKey").And.DoesNotContain("token");
    }

    [Test]
    public async Task Performance_alerts_use_consistent_ratios_and_minimum_samples()
    {
        var rules = await File.ReadAllTextAsync(Path.Combine(
            RepositoryRoot, "deploy", "shared", "observability", "prometheus", "rules",
            "recording-rules.yml"));
        var alerts = await File.ReadAllTextAsync(Path.Combine(
            RepositoryRoot, "deploy", "shared", "observability", "prometheus", "rules",
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
        await Assert.That(alerts).Contains(
            "noctf:leaderboard_projection_samples:count5m >= 3");
        await Assert.That(alerts).Contains(
            "noctf_runner_capacity_transaction_exhaustions_total[5m]");
        await Assert.That(alerts).Contains("NoCtfDiskSpaceWarning");
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
