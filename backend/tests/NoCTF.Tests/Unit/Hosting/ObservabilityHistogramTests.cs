using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NoCTF.Application.Observability;
using NoCTF.Domain.Gameplay;
using NoCTF.Hosting.Observability;
using OpenTelemetry.Metrics;

namespace NoCTF.Tests.Unit.Hosting;

[NotInParallel]
public sealed class ObservabilityHistogramTests
{
    [Test]
    public async Task Real_meter_provider_exports_second_buckets_counts_and_reasonable_millisecond_quantiles()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddNoCtfObservability(builder.Configuration, "histogram-test");
        await using var app = builder.Build();
        app.Use((context, next) => { context.Connection.LocalPort = 9464; return next(context); });
        app.UseNoCtfObservability();
        await app.StartAsync();
        foreach (var sample in new[] { 0.001, 0.002, 0.004, 0.012, 0.04 })
        {
            NoCtfTelemetry.RecordApiRequest("histogram-test", "success", sample);
            NoCtfTelemetry.RecordNatsOperation("histogram-test", "success", sample);
        }
        NoCtfTelemetry.RecordSignalRPublish("histogram-test", "success", 0.012);
        NoCtfTelemetry.RecordRunnerClaim("histogram-test", "success", 2, 0.012);
        NoCtfTelemetry.RecordLeaderboardProjection("histogram-test", "success", 0.012, 12, 40);
        NoCtfTelemetry.RecordSchedulerRebuild("histogram-test", 0.012, 40);
        NoCtfTelemetry.RecordSchedulerDispatch("histogram-test", "success", 0.012);
        NoCtfTelemetry.RecordGameplayFactSubmissions(GameplayFactKind.FlagAttempt, 4);
        NoCtfTelemetry.RecordGameplayFactSubmissions(GameplayFactKind.BreakAttempt, 2);
        NoCtfTelemetry.RecordGameplayFactSubmissions(GameplayFactKind.FixAttempt);
        NoCtfTelemetry.RecordGameplayFactProcessing(
            GameplayFactKind.FlagAttempt,
            GameplayFactState.Completed,
            GameplayFactResult.Correct,
            0.012);
        NoCtfTelemetry.RecordGameplayFactProcessing(
            GameplayFactKind.BreakAttempt,
            GameplayFactState.Completed,
            GameplayFactResult.Wrong,
            0.04);
        NoCtfTelemetry.RecordGameplayFactProcessing(
            GameplayFactKind.FlagAttempt,
            GameplayFactState.PlatformFailed,
            null,
            0.8);
        NoCtfTelemetry.RecordGameplayFactStage(
            GameplayFactPerformanceStage.ChallengeAttemptStateRead, 0.012);
        NoCtfTelemetry.RecordGameplayFactStage(
            GameplayFactPerformanceStage.AdmissionLoad, 0.04);
        NoCtfTelemetry.RecordRuntimeDispatchStage(
            RuntimeDispatchPerformanceStage.TargetRead, 0.012);
        using var db = new DbContext(new DbContextOptionsBuilder<DbContext>()
            .UseNpgsql("Host=localhost;Database=telemetry_test;Username=unused;Password=unused")
            .Options);
        _ = db.ChangeTracker.HasChanges();
        app.Services.GetRequiredService<MeterProvider>().ForceFlush();
        using var client = app.GetTestClient();
        var exported = await client.GetStringAsync("/metrics");

        foreach (var prefix in new[] { "api_request", "nats_operation", "signalr_publish", "runner_claim",
            "leaderboard_projection", "scheduler_rebuild", "scheduler_dispatch" })
        {
            var suffix = prefix == "scheduler_dispatch" ? "lateness" : "duration";
            var metric = $"noctf_{prefix}_{suffix}_seconds";
            var buckets = Buckets(exported, metric);
            await Assert.That(buckets.Keys).Contains(0.05);
            await Assert.That(buckets.Keys).Contains(0.8);
            await Assert.That(buckets.Keys).Contains(300);
            if (prefix is "api_request" or "nats_operation")
            {
                await Assert.That(buckets[0.005]).IsEqualTo(3);
                await Assert.That(buckets[0.05]).IsEqualTo(5);
                await Assert.That(Estimate(buckets, prefix == "api_request" ? 0.95 : 0.99) * 1000)
                    .IsBetween(40, 50);
            }
            else await Assert.That(buckets[0.025]).IsEqualTo(1);
        }
        // Count histograms must not inherit the second-based view.
        await Assert.That(exported.Split('\n').Any(line => line.StartsWith("noctf_leaderboard_projection_teams", StringComparison.Ordinal)
            && line.Contains("le=\"0.05\"", StringComparison.Ordinal))).IsFalse();
        await Assert.That(exported.Split('\n').Any(line => line.StartsWith("noctf_leaderboard_projection_teams", StringComparison.Ordinal)
            && line.Contains("le=\"50\"", StringComparison.Ordinal))).IsTrue();
        var submissionLines = exported.Split('\n').Where(line => line.StartsWith(
            "noctf_gameplay_fact_submissions_total{",
            StringComparison.Ordinal)).ToArray();
        await Assert.That(submissionLines.Any(line => line.Contains(
            "kind=\"flag\"",
            StringComparison.Ordinal))).IsTrue();
        await Assert.That(submissionLines.Any(line => line.Contains(
            "kind=\"break\"",
            StringComparison.Ordinal))).IsTrue();
        await Assert.That(submissionLines.Any(line => line.Contains(
            "kind=\"fix\"",
            StringComparison.Ordinal))).IsTrue();
        var processingLines = exported.Split('\n').Where(line => line.StartsWith(
            "noctf_gameplay_fact_processing_total{",
            StringComparison.Ordinal)).ToArray();
        await Assert.That(processingLines.Any(line => line.Contains(
            "outcome=\"correct\"",
            StringComparison.Ordinal))).IsTrue();
        await Assert.That(processingLines.Any(line => line.Contains(
            "outcome=\"incorrect\"",
            StringComparison.Ordinal))).IsTrue();
        await Assert.That(processingLines.Any(line => line.Contains(
            "outcome=\"platform_error\"",
            StringComparison.Ordinal))).IsTrue();
        await Assert.That(exported).Contains(
            "noctf_gameplay_fact_processing_duration_seconds_bucket");
        await Assert.That(exported.Split('\n').Any(line =>
            line.StartsWith("noctf_gameplay_fact_stage_duration_seconds_bucket{", StringComparison.Ordinal)
            && line.Contains("stage=\"ChallengeAttemptStateRead\"", StringComparison.Ordinal))).IsTrue();
        await Assert.That(exported.Split('\n').Any(line =>
            line.StartsWith("noctf_runtime_dispatch_stage_duration_seconds_bucket{", StringComparison.Ordinal)
            && line.Contains("stage=\"TargetRead\"", StringComparison.Ordinal))).IsTrue();
        await Assert.That(exported).Contains(
            "microsoft_entityframeworkcore_active_dbcontexts");
    }

    private static SortedDictionary<double, double> Buckets(string text, string metric)
    {
        var result = new SortedDictionary<double, double>();
        foreach (var line in text.Split('\n').Where(line => line.StartsWith(metric + "_bucket{", StringComparison.Ordinal)
            && line.Contains("histogram-test", StringComparison.Ordinal)))
        {
            var boundary = Regex.Match(line, "le=\"([^\"]+)\"").Groups[1].Value;
            var value = double.Parse(line[(line.IndexOf('}') + 1)..].Trim().Split(' ')[0], CultureInfo.InvariantCulture);
            result[boundary == "+Inf" ? double.PositiveInfinity : double.Parse(boundary, CultureInfo.InvariantCulture)] = value;
        }
        return result;
    }

    // Classic Prometheus histogram interpolation, tested as an interval rather than exact raw quantiles.
    private static double Estimate(SortedDictionary<double, double> buckets, double quantile)
    {
        var rank = buckets[double.PositiveInfinity] * quantile;
        double previousCount = 0, previousBound = 0;
        foreach (var (bound, count) in buckets)
        {
            if (count >= rank) return previousBound + (bound - previousBound) * (rank - previousCount) / (count - previousCount);
            previousCount = count;
            previousBound = bound;
        }
        throw new InvalidOperationException("No finite histogram quantile.");
    }
}
