using System.Globalization;
using System.Reflection;
using System.Runtime;
using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using NoCTF.Infrastructure.Persistence;
using NoCTF.Tests.Fixtures.PostgreSql;

namespace NoCTF.Tests.Integration.Persistence;

public sealed partial class StaffWebhookCaptureScopeTests
{
    [Test, Category("Performance"), NotInParallel, Timeout(600_000)]
    public async Task Measure_capture_with_interleaved_full_and_scoped_history_reads(CancellationToken ct)
    {
        const string environmentVariable = "NOCTF_STAFF_CAPTURE_MEASUREMENTS";
        var directory = Environment.GetEnvironmentVariable(environmentVariable);
        if (string.IsNullOrWhiteSpace(directory))
        {
            Skip.Test($"Set {environmentVariable} to an ignored artifact directory to run this isolated component diagnostic.");
            return;
        }
        if (typeof(StaffWebhookCaptureScopeTests).Assembly.GetCustomAttribute<AssemblyConfigurationAttribute>()?.Configuration != "Release")
            throw new InvalidOperationException("Staff capture measurements require a Release build.");
        await DockerIntegrationTest.RunAsync(async () =>
        {
            const int warmupPairs = 3;
            const int measuredPairs = 10;
            var samples = new List<CaptureMeasurement>();
            var diagnostics = new List<CaptureReadMeasurement>();
            var queries = new Dictionary<string, IReadOnlyList<ProjectionReadCommand>>();
            var shapes = new[] { new CaptureCorpus(32, 32, 16), new CaptureCorpus(512, 32, 256) };
            foreach (var shape in shapes)
            {
                await using var postgres = new PostgreSqlContainerFixture(); await postgres.InitializeAsync();
                var options = Options(postgres);
                var fixture = await SeedAsync(options, shape.Consultations, shape.RepliesPerConsultation, shape.Incidents, ct);
                var expected = (await CaptureOnlyAsync(options, fixture, true, null, ct)).CanonicalEvents;
                for (var pair = -warmupPairs; pair < measuredPairs; pair++)
                {
                    foreach (var full in pair % 2 == 0 ? new[] { true, false } : new[] { false, true })
                    {
                        var observation = await CaptureOnlyAsync(options, fixture, full, null, ct);
                        await Assert.That(observation.CanonicalEvents).IsEqualTo(expected);
                        await Assert.That(observation.EventCount).IsEqualTo(1);
                        if (pair < 0) continue;
                        var variant = full ? "full-capture-reference" : "scoped-capture";
                        samples.Add(new(shape, pair, variant, observation.Milliseconds, observation.AllocatedBytes,
                            observation.EventCount, observation.Collections[0], observation.Collections[1], observation.Collections[2]));
                    }
                }
                // Count reads separately: the per-row diagnostic wrapper is absent from timing/allocation pairs.
                var reads = new ProjectionReadMetrics();
                var diagnosticOptions = new DbContextOptionsBuilder<NoCtfDbContext>(options).AddInterceptors(reads).Options;
                foreach (var full in new[] { true, false })
                {
                    var observation = await CaptureOnlyAsync(diagnosticOptions, fixture, full, reads, ct);
                    await Assert.That(observation.CanonicalEvents).IsEqualTo(expected);
                    var variant = full ? "full-capture-reference" : "scoped-capture";
                    diagnostics.Add(new(shape, variant, observation.Commands.Count, observation.ReturnedRows, observation.ReturnedCells));
                    queries[$"{shape.Consultations}-{variant}"] = observation.Commands;
                }
            }
            Directory.CreateDirectory(directory);
            var metadata = new
            {
                Command = Environment.CommandLine,
                AssemblyVersion = typeof(StaffWebhookCaptureScopeTests).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion,
                Runtime = RuntimeInformation.FrameworkDescription, RuntimeInformation.OSDescription,
                Architecture = RuntimeInformation.ProcessArchitecture.ToString(), Environment.ProcessorCount,
                ServerGC = GCSettings.IsServerGC, GCSettings.LatencyMode,
                PostgreSqlImage = "postgres:17.10-alpine3.24@sha256:742f40ea20b9ff2ff31db5458d127452988a2164df9e17441e191f3b72252193",
                Workload = "One participant reply on a fixed 33-node existing consultation; unrelated consultation/incident history grows; active subscription and canonical baseline exist",
                Corpus = shapes, WarmupPairs = warmupPairs, MeasuredPairs = measuredPairs,
                Isolation = "Serializable transaction rolled back after capture; same database and equivalent pending mutation for both variants",
                Limitations = new[]
                {
                    "Component diagnostic, not a platform load, QPS, tail-latency or maximum sustainable throughput measurement.",
                    "Timing and allocation cover same-transaction capture only, excluding context/transaction creation, source mutation preparation, seeding, SaveChanges/commit, delivery and result assertions.",
                    "Reference uses the retained full projection/capture path plus the same enabled-subscription probe; it isolates scoping and is not a separate historical binary.",
                    "Allocation and GC counters are process-wide, including test-framework/background work; run this test alone.",
                    "Warmup/timed pairs have no query interceptor; SQL and read counts come from separate instrumented passes after timing.",
                    "Returned rows/cells count materialized reader rows and row width, not rows scanned by PostgreSQL or network bytes.",
                    "Interleaved warmed reads use PostgreSQL buffers, connection pools and EF caches; no production performance guarantee."
                },
                Samples = samples, Diagnostics = diagnostics, Queries = queries
            };
            await File.WriteAllTextAsync(Path.Combine(directory, "staff-webhook-capture.json"),
                JsonSerializer.Serialize(metadata, new JsonSerializerOptions { WriteIndented = true }), ct);
            var csv = new List<string> { "consultations,replies_per_consultation,incidents,pair,variant,elapsed_ms,allocated_bytes,events,gen0,gen1,gen2" };
            csv.AddRange(samples.Select(value => string.Join(',', value.Corpus.Consultations, value.Corpus.RepliesPerConsultation, value.Corpus.Incidents,
                value.Pair, value.Variant, value.Milliseconds.ToString("F3", CultureInfo.InvariantCulture), value.AllocatedBytes,
                value.EventCount, value.Gen0, value.Gen1, value.Gen2)));
            await File.WriteAllLinesAsync(Path.Combine(directory, "staff-webhook-capture.csv"), csv, ct);
            var readCsv = new List<string> { "consultations,replies_per_consultation,incidents,variant,queries,returned_rows,returned_cells" };
            readCsv.AddRange(diagnostics.Select(value => string.Join(',', value.Corpus.Consultations, value.Corpus.RepliesPerConsultation,
                value.Corpus.Incidents, value.Variant, value.Queries, value.ReturnedRows, value.ReturnedCells)));
            await File.WriteAllLinesAsync(Path.Combine(directory, "staff-webhook-capture-reads.csv"), readCsv, ct);
        });
    }

    private sealed record CaptureCorpus(int Consultations, int RepliesPerConsultation, int Incidents);
    private sealed record CaptureMeasurement(CaptureCorpus Corpus, int Pair, string Variant, double Milliseconds, long AllocatedBytes,
        int EventCount, int Gen0, int Gen1, int Gen2);
    private sealed record CaptureReadMeasurement(CaptureCorpus Corpus, string Variant, int Queries, long ReturnedRows, long ReturnedCells);
}
