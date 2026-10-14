using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Reflection;
using System.Runtime;
using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Observability;
using NoCTF.Application.Runtime.Capacity;
using NoCTF.Domain.Runtime;
using NoCTF.Infrastructure.Persistence;
using NoCTF.Tests.Integration.Persistence;

namespace NoCTF.Tests.Integration.Runtime;

public sealed partial class RuntimeCapacityDomainTests
{
    [Test, Category("Performance"), Timeout(900_000)]
    public async Task Measure_interleaved_full_and_narrow_allocation_reads_under_global_coordination(CancellationToken ct)
    {
        var directory = Environment.GetEnvironmentVariable("NOCTF_CAPACITY_DOMAIN_MEASUREMENTS");
        if (string.IsNullOrWhiteSpace(directory))
        {
            Skip.Test("Set NOCTF_CAPACITY_DOMAIN_MEASUREMENTS to an ignored directory for isolated component measurements.");
            return;
        }
        if (typeof(RuntimeCapacityDomainTests).Assembly.GetCustomAttribute<AssemblyConfigurationAttribute>()?.Configuration != "Release")
            throw new InvalidOperationException("Capacity component measurements require Release.");
        await DockerIntegrationTest.RunAsync(async () =>
        {
            const int requests = 128;
            const int concurrency = 16;
            const int warmupPairs = 2;
            const int measuredPairs = 5;
            var samples = new List<CapacityMeasurement>();
            var warmups = new List<CapacityMeasurement>();
            var diagnostics = new List<CapacityDiagnostic>();
            foreach (var domains in new[] { 1, 4 })
            {
                await using var fixture = await RuntimeCapacityDomainFixture.CreateAsync(requests, ct);
                for (var domain = 0; domain < domains; domain++)
                    await fixture.AddOwnerAsync($"runner-{domain}", $"domain-{domain}",
                        new(requests * 128L, requests * 10L, requests), requests, ct);
                var global = new GlobalCapacityLedgerCoordinator();
                for (var pair = -warmupPairs; pair < measuredPairs; pair++)
                {
                    foreach (var reference in pair % 2 == 0 ? new[] { true, false } : new[] { false, true })
                    {
                        var result = await RunCapacityPassAsync(fixture, global, reference, domains, concurrency,
                            pair, diagnostic: false, ct);
                        if (pair >= 0) samples.Add(result.Measurement);
                        else warmups.Add(result.Measurement);
                    }
                }
                // Reader wrapping/SQL collection is deliberately absent from timed pairs.
                foreach (var reference in new[] { true, false })
                {
                    var result = await RunCapacityPassAsync(fixture, global, reference, domains, concurrency: 1,
                        pair: 0, diagnostic: true, ct);
                    diagnostics.Add(new(domains, result.Measurement.Variant, result.Measurement.Requested,
                        result.Measurement.Completed, result.Measurement.Exceptions, result.Measurement.InvariantValid,
                        result.ReaderCommands, result.Rows, result.Cells, result.Queries, result.Measurement.Failures,
                        result.Measurement.FailureDetails));
                }
            }
            Directory.CreateDirectory(directory);
            await File.WriteAllTextAsync(Path.Combine(directory, "capacity-domain-transactions.json"),
                JsonSerializer.Serialize(new
                {
                    Command = Environment.CommandLine,
                    AssemblyVersion = typeof(RuntimeCapacityDomainTests).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion,
                    Runtime = RuntimeInformation.FrameworkDescription, RuntimeInformation.OSDescription,
                    Architecture = RuntimeInformation.ProcessArchitecture.ToString(), Environment.ProcessorCount,
                    ServerGC = GCSettings.IsServerGC, GCSettings.LatencyMode,
                    RuntimeCapacityDomainFixture.PostgresImage, RuntimeCapacityDomainFixture.NatsImage,
                    RequestsPerPass = requests, Concurrency = concurrency, Domains = new[] { 1, 4 },
                    WarmupPairs = warmupPairs, MeasuredPairs = measuredPairs,
                    Workload = "Queued template Runtime primary claims pinned evenly to real NATS resource-domain owners; unchanged observations have enough capacity for all requests; successful allocations are released between passes",
                    Baseline = "75f8790b claim/release algorithm with its original global Id1 ledger and local semaphore; candidate retains global coordination and uses narrow domain-wide reservation reads; current allocation-domain index exists for both variants",
                    RetryPolicy = "Reference retains three attempts with 2..17ms retry jitter; candidate permits eight attempts with exponentially increasing 1..255ms jitter (at most 1001ms total scheduled delay plus I/O). Exhaustion remains a failure. Both variants use the existing seeded global fence; measured pairs follow warmup. Independent-process contention is covered separately by correctness tests.",
                    Limitations = new[]
                    {
                        "Same-schema algorithm reference, not a historical binary or full-platform maximum sustainable throughput measurement.",
                        "Timed scope includes context creation, NATS registration reads, allocation SQL, SaveChanges and commit; fixture seeding, ownership refresh, validation and release are excluded.",
                        "No provider containers are created; release wakeup publication is a no-op in both variants. All Runner domains are isolated fixture domains, never fabricated divisions of one production cluster.",
                        "SELECT/row/cell diagnostics run separately at concurrency 1; write RETURNING readers remain native to Npgsql; reported timing pairs use no command/reader interceptor.",
                        "Rows/cells mean materialized reads rather than database scanned rows or network bytes; client CPU/allocations/GC are process-wide and exclude dependency CPU.",
                        "One owner per domain is stable during measurements. Owner handoff and competing identity safety have separate PostgreSQL/NATS correctness tests.",
                        "Both variants retain Serializable and existing admission limits; observations are refreshed before each pass. Rejections, exceptions or an invariant mismatch invalidate a sample."
                    },
                    Samples = samples, Warmups = warmups, Diagnostics = diagnostics
                }, new JsonSerializerOptions { WriteIndented = true }), ct);
            await Assert.That(samples.Concat(warmups).All(sample => sample.Completed == requests
                && sample.Exceptions == 0 && sample.InvariantValid)).IsTrue();
            await Assert.That(diagnostics.All(sample => sample.Completed == sample.Requested
                && sample.Exceptions == 0 && sample.InvariantValid)).IsTrue();
        });
    }

    private static async Task<CapacityPass> RunCapacityPassAsync(RuntimeCapacityDomainFixture fixture,
        GlobalCapacityLedgerCoordinator global, bool reference, int domains, int concurrency,
        int pair, bool diagnostic, CancellationToken ct)
    {
        await fixture.RefreshOwnersAsync(ct);
        var durations = new ConcurrentBag<double>();
        var outcomes = new ConcurrentBag<string>();
        var failureDetails = new ConcurrentBag<string>();
        var readers = new ConcurrentBag<ProjectionReadCommand>();
        var completed = 0;
        var exceptions = 0;
        long retries = 0;
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, meterListener) =>
        {
            if (instrument.Meter.Name == NoCtfTelemetry.MeterName
                && instrument.Name == "noctf.runner.capacity.transaction_retries")
                meterListener.EnableMeasurementEvents(instrument);
        };
        listener.SetMeasurementEventCallback<long>((_, value, tags, _) =>
        {
            foreach (var tag in tags)
                if (tag.Key == "operation" && Equals(tag.Value, "claim")) Interlocked.Add(ref retries, value);
        });
        listener.Start();
        using var process = Process.GetCurrentProcess();
        var cpu = process.TotalProcessorTime;
        var allocated = GC.GetTotalAllocatedBytes(precise: true);
        var collections = Enumerable.Range(0, 3).Select(GC.CollectionCount).ToArray();
        var started = Stopwatch.GetTimestamp();
        await Parallel.ForEachAsync(fixture.RuntimeIds.Select((id, index) => (id, index)),
            new ParallelOptions { MaxDegreeOfParallelism = concurrency, CancellationToken = ct }, async (item, token) =>
        {
            var oneStarted = Stopwatch.GetTimestamp();
            ProjectionReadMetrics? metrics = diagnostic ? new() : null;
            var options = metrics is null ? fixture.Options
                : new DbContextOptionsBuilder<NoCtfDbContext>(fixture.Options).AddInterceptors(metrics).Options;
            try
            {
                await using var db = new NoCtfDbContext(options);
                IRunnerCapacityGate gate = reference ? fixture.BaselineGate(db, global) : fixture.Gate(db);
                var claim = await gate.TryClaimForRunnerAsync(
                    new(item.id, RuntimeCapacityDomainFixture.Pool, 128, 10, 1), $"runner-{item.index % domains}", token);
                if (claim.Availability == RunnerCapacityAvailability.Claimed && claim.State == RunnerCapacityClaimState.Acquired)
                    Interlocked.Increment(ref completed);
                else outcomes.Add($"{claim.Availability}/{claim.State}/{claim.Failure}");
            }
            catch (Exception exception) when (!token.IsCancellationRequested)
            {
                Interlocked.Increment(ref exceptions);
                outcomes.Add(exception.GetType().Name);
                // This fixture contains only generated synthetic data, never production payloads.
                var detail = $"{exception.GetType().Name}: {exception.GetBaseException().GetType().Name}: {exception.GetBaseException().Message}";
                failureDetails.Add(detail.Length > 2048 ? detail[..2048] : detail);
            }
            finally
            {
                durations.Add(Stopwatch.GetElapsedTime(oneStarted).TotalMilliseconds);
                if (metrics is not null)
                    foreach (var command in metrics.Commands) readers.Add(command);
            }
        });
        var milliseconds = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        var cpuMilliseconds = (process.TotalProcessorTime - cpu).TotalMilliseconds;
        var allocatedBytes = GC.GetTotalAllocatedBytes(precise: true) - allocated;
        var gc = Enumerable.Range(0, 3).Select(generation => GC.CollectionCount(generation) - collections[generation]).ToArray();
        listener.Dispose();
        bool invariant;
        await using (var verify = new NoCtfDbContext(fixture.Options))
        {
            var rows = await verify.RuntimeInstances.AsNoTracking().IgnoreAutoIncludes()
                .SelectMany(item => item.CapacityAllocationEntries).ToArrayAsync(ct);
            invariant = rows.Length == completed && rows.GroupBy(row => row.ResourceDomain).Count() == domains
                && rows.All(row => row.ResourceDomain == "domain-" + row.RunnerId["runner-".Length..]
                    && row.LimitMemoryBytes == 128 && row.LimitCpuMillicores == 10 && row.LimitPidsLimit == 1)
                && rows.GroupBy(row => row.ResourceDomain).All(group => group.Count() <= fixture.RuntimeIds.Length / domains);
        }
        // Excluded from measurements; preserve normal release semantics before the paired variant.
        for (var index = 0; index < fixture.RuntimeIds.Length; index++)
            await fixture.ReleaseAsync(fixture.RuntimeIds[index], $"runner-{index % domains}", ct);
        var sorted = durations.Order().ToArray();
        var measurement = new CapacityMeasurement(domains, pair, reference ? "global-ledger-reference" : "global-ledger-narrow-projection",
            fixture.RuntimeIds.Length, completed, exceptions, retries, invariant, milliseconds,
            completed * 1000d / milliseconds, Percentile(sorted, .5), Percentile(sorted, .95), Percentile(sorted, .99),
            cpuMilliseconds, allocatedBytes, gc, outcomes.GroupBy(value => value).ToDictionary(group => group.Key, group => group.Count()),
            failureDetails.Distinct().Take(16).ToArray());
        return new(measurement, readers.Count, readers.Sum(reader => reader.Rows), readers.Sum(reader => reader.Rows * reader.Columns),
            readers.Select(reader => reader.Sql).Distinct().ToArray());
    }

    private static double Percentile(double[] values, double fraction) => values[(int)Math.Ceiling(values.Length * fraction) - 1];

    private sealed record CapacityMeasurement(int Domains, int Pair, string Variant, int Requested, int Completed,
        int Exceptions, long TransactionRetries, bool InvariantValid, double Milliseconds, double CompletedPerSecond,
        double P50Milliseconds, double P95Milliseconds, double P99Milliseconds, double ClientCpuMilliseconds,
        long ClientAllocatedBytes, int[] Collections, IReadOnlyDictionary<string, int> Failures, string[] FailureDetails);
    private sealed record CapacityDiagnostic(int Domains, string Variant, int Requested, int Completed, int Exceptions,
        bool InvariantValid, int ReaderCommands, long ReturnedRows, long ReturnedCells, string[] Queries,
        IReadOnlyDictionary<string, int> Failures, string[] FailureDetails);
    private sealed record CapacityPass(CapacityMeasurement Measurement, int ReaderCommands, long Rows, long Cells, string[] Queries);
}
