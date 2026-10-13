using System.Data;
using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Runtime;
using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Competitions.Awd;
using NoCTF.Application.Scoring.Leaderboard;
using NoCTF.Domain.Challenges;
using NoCTF.Infrastructure.Persistence;
using NoCTF.Infrastructure.Scoring.Leaderboard;
using Testcontainers.PostgreSql;

namespace NoCTF.Tests.Integration.Persistence;

[Category("Integration")]
public sealed partial class AwdScoreProjectionReaderPersistenceTests
{
    private const string PostgresImage = "postgres:17.10-alpine3.24@sha256:742f40ea20b9ff2ff31db5458d127452988a2164df9e17441e191f3b72252193";
    private const string MeasurementsEnvironmentVariable = "NOCTF_AWD_PROJECTION_MEASUREMENTS";

    [Test, Timeout(300_000)]
    public Task Narrow_projection_matches_full_entity_oracle_across_history_cutoffs_and_rule_overrides(CancellationToken ct) =>
        DockerIntegrationTest.RunAsync(async () =>
        {
            await using var postgres = new PostgreSqlBuilder("postgres:17.10-alpine3.24@sha256:742f40ea20b9ff2ff31db5458d127452988a2164df9e17441e191f3b72252193").Build();
            await postgres.StartAsync(ct);
            var options = Options(postgres);
            var fixture = await SeedAsync(options, new(6, 3, 8), ct);
            var reads = new ProjectionReadMetrics();
            var countedOptions = new DbContextOptionsBuilder<NoCtfDbContext>(options).AddInterceptors(reads).Options;
            foreach (var at in new[]
            {
                fixture.Start.AddTicks(-1), fixture.Start.AddSeconds(15), fixture.PauseAt.AddSeconds(10),
                fixture.End.AddSeconds(-15), fixture.End.AddSeconds(3), fixture.End.AddSeconds(30)
            })
            {
                var baseline = await ReadAsync(countedOptions, fixture, at, baseline: true, null, ct);
                reads.Reset();
                var actual = await ReadAsync(countedOptions, fixture, at, baseline: false, null, ct);
                await Assert.That(Canonical(actual)).IsEqualTo(Canonical(baseline));
                await Assert.That(reads.Commands.All(command => !command.Sql.Contains("protected_read_context", StringComparison.OrdinalIgnoreCase)
                    && !command.Sql.Contains("injection_command", StringComparison.OrdinalIgnoreCase)
                    && !command.Sql.Contains("acquisition_evidence", StringComparison.OrdinalIgnoreCase))).IsTrue();
            }
            var candidate = new AwdScoringProjectionOverride(fixture.Challenges[1].Id, 2, 6,
                fixture.PreviewPolicy);
            var before = await ReadAsync(options, fixture, fixture.End.AddSeconds(30), true, candidate, ct);
            var after = await ReadAsync(options, fixture, fixture.End.AddSeconds(30), false, candidate, ct);
            await Assert.That(Canonical(after)).IsEqualTo(Canonical(before));
            await Assert.That(after.Any(score => score.Components.Any(component => component.Points != 0))).IsTrue();
            // Deleted questions and bounded scoring windows must still affect captures and SLA independently.
            var filtered = fixture with
            {
                Challenges = fixture.Challenges.Select((challenge, index) => index == 0
                    ? challenge with { IsDeleted = true }
                    : challenge with { Timing = new ChallengeTiming(ScoringEndsAt: fixture.Start.AddSeconds(190)) }).ToArray()
            };
            await Assert.That(Canonical(await ReadAsync(options, filtered, fixture.End.AddSeconds(30), false, null, ct)))
                .IsEqualTo(Canonical(await ReadAsync(options, filtered, fixture.End.AddSeconds(30), true, null, ct)));
        });

    [Test, Category("Performance"), NotInParallel, Timeout(600_000)]
    public async Task Measure_full_history_projection_with_interleaved_equivalent_readers(CancellationToken ct)
    {
        var directory = Environment.GetEnvironmentVariable(MeasurementsEnvironmentVariable);
        if (string.IsNullOrWhiteSpace(directory))
        {
            Skip.Test($"Set {MeasurementsEnvironmentVariable} to an ignored artifact directory to run the isolated component experiment.");
            return;
        }
        if (typeof(AwdScoreProjectionReaderPersistenceTests).Assembly.GetCustomAttribute<AssemblyConfigurationAttribute>()?.Configuration != "Release")
            throw new InvalidOperationException("AWD projection measurements require a Release build.");
        await DockerIntegrationTest.RunAsync(async () =>
        {
            var revision = await ReadMetadataCommandAsync("git", ["rev-parse", "HEAD"], ct);
            var dirtyState = await ReadMetadataCommandAsync("git", ["status", "--short"], ct);
            var sdk = await ReadMetadataCommandAsync("dotnet", ["--info"], ct);
            await using var postgres = new PostgreSqlBuilder("postgres:17.10-alpine3.24@sha256:742f40ea20b9ff2ff31db5458d127452988a2164df9e17441e191f3b72252193").Build();
            await postgres.StartAsync(ct);
            var shape = new ProjectionCorpusShape(24, 4, 24);
            var options = Options(postgres);
            var fixture = await SeedAsync(options, shape, ct);
            var reads = new ProjectionReadMetrics();
            var diagnosticOptions = new DbContextOptionsBuilder<NoCtfDbContext>(options).AddInterceptors(reads).Options;
            var at = fixture.End.AddSeconds(30);
            var expected = Canonical(await ReadAsync(options, fixture, at, true, null, ct));
            const int warmupPairs = 3;
            const int measuredPairs = 10;
            var measurements = new List<ProjectionMeasurement>();
            for (var pair = -warmupPairs; pair < measuredPairs; pair++)
            {
                // Alternate order to avoid assigning every cache/thermal/order advantage to one reader.
                foreach (var baseline in pair % 2 == 0 ? new[] { true, false } : new[] { false, true })
                {
                    var variant = baseline ? "full-entity-baseline-75f8790b" : "narrow-projection";
                    var allocatedBefore = GC.GetTotalAllocatedBytes(precise: true);
                    var gcBefore = Enumerable.Range(0, 3).Select(GC.CollectionCount).ToArray();
                    var started = Stopwatch.GetTimestamp();
                    var result = await ReadAsync(options, fixture, at, baseline, null, ct);
                    var milliseconds = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
                    var allocated = GC.GetTotalAllocatedBytes(precise: true) - allocatedBefore;
                    var collections = Enumerable.Range(0, 3).Select(generation => GC.CollectionCount(generation) - gcBefore[generation]).ToArray();
                    await Assert.That(Canonical(result)).IsEqualTo(expected);
                    if (pair < 0) continue;
                    measurements.Add(new(pair, variant, milliseconds, allocated,
                        result.Count, collections[0], collections[1], collections[2]));
                }
            }
            // Reader wrappers change per-row cost. Collect shape evidence separately from all clean timings.
            var diagnostics = new List<ProjectionDiagnostic>();
            foreach (var baseline in new[] { true, false })
            {
                reads.Reset();
                var result = await ReadAsync(diagnosticOptions, fixture, at, baseline, null, ct);
                await Assert.That(Canonical(result)).IsEqualTo(expected);
                diagnostics.Add(new(baseline ? "full-entity-baseline-75f8790b" : "narrow-projection",
                    reads.Commands.Count, reads.Commands.Sum(command => command.Rows),
                    reads.Commands.Sum(command => command.Rows * command.Columns), result.Count, reads.Commands.ToArray()));
            }
            Directory.CreateDirectory(directory);
            var metadata = new
            {
                BaselineCommit = "75f8790b", ProductionReader = nameof(AwdScoreProjectionReader),
                SourceRevision = revision, DirtyState = dirtyState, InstalledSdkAndRuntime = sdk,
                Command = Environment.CommandLine,
                AssemblyVersion = typeof(AwdScoreProjectionReaderPersistenceTests).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion,
                Runtime = RuntimeInformation.FrameworkDescription, RuntimeInformation.OSDescription,
                Architecture = RuntimeInformation.ProcessArchitecture.ToString(), Environment.ProcessorCount,
                ServerGC = GCSettings.IsServerGC, GCSettings.LatencyMode,
                Deployment = "Release test executable; isolated PostgreSQL Testcontainer; same process and same seeded database for both readers",
                PostgreSqlImage = PostgresImage, Corpus = shape, fixture.Counts, WarmupPairs = warmupPairs, MeasuredPairs = measuredPairs,
                Observation = at, Isolation = IsolationLevel.RepeatableRead.ToString(),
                Limitations = new[]
                {
                    "Component measurements include context, connection, transaction, relational reads and full scoring; exclude seeding and output validation.",
                    "Elapsed/allocation runs have no reader interceptor; SQL/row/cell diagnostics come from separate equivalent reads after measurements.",
                    "Allocation totals are process-wide and may include test-framework/background allocations; run this test alone.",
                    "Read rows count successful DbDataReader.Read operations; cells are returned row width, not network bytes or database rows scanned.",
                    "Warm PostgreSQL buffers, client pools and EF query caches; no production QPS or end-to-end latency claim."
                },
                Measurements = measurements,
                Diagnostics = diagnostics
            };
            await File.WriteAllTextAsync(Path.Combine(directory, "awd-full-history-projection.json"),
                JsonSerializer.Serialize(metadata, new JsonSerializerOptions { WriteIndented = true }), ct);
            var csv = new List<string> { "pair,reader,elapsed_ms,allocated_bytes,score_rows,gen0,gen1,gen2" };
            csv.AddRange(measurements.Select(value => string.Join(",", value.Pair, value.Reader,
                value.ElapsedMilliseconds.ToString("F3", CultureInfo.InvariantCulture), value.AllocatedBytes,
                value.ScoreRows, value.Gen0, value.Gen1, value.Gen2)));
            await File.WriteAllLinesAsync(Path.Combine(directory, "awd-full-history-projection.csv"), csv, ct);
            await File.WriteAllLinesAsync(Path.Combine(directory, "awd-full-history-projection-reads.csv"),
                new[] { "reader,queries,returned_rows,returned_cells,score_rows" }.Concat(diagnostics.Select(value =>
                    string.Join(",", value.Reader, value.Queries, value.ReturnedRows, value.ReturnedCells, value.ScoreRows))), ct);
        });
    }

    private static DbContextOptions<NoCtfDbContext> Options(PostgreSqlContainer postgres) =>
        new DbContextOptionsBuilder<NoCtfDbContext>().UseNpgsql(postgres.GetConnectionString())
            .UseSnakeCaseNamingConvention().Options;

    private static async Task<IReadOnlyList<AwdRoundScoreProjection>> ReadAsync(DbContextOptions<NoCtfDbContext> options,
        ProjectionFixture fixture, DateTimeOffset at, bool baseline, AwdScoringProjectionOverride? candidate, CancellationToken ct)
    {
        await using var db = new NoCtfDbContext(options);
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, ct);
        var result = baseline
            ? await AwdScoreProjectionReaderBaseline.ReadAsync(db, fixture.CompetitionId, fixture.Challenges, fixture.Teams, at, ct, candidate)
            : await AwdScoreProjectionReader.ReadAsync(db, fixture.CompetitionId, fixture.Challenges, fixture.Teams, at, ct, candidate);
        await transaction.CommitAsync(ct);
        return result;
    }

    private static string Canonical(IReadOnlyList<AwdRoundScoreProjection> scores) => JsonSerializer.Serialize(scores
        .OrderBy(score => score.RoundNumber).ThenBy(score => score.CompetitionChallengeId).ThenBy(score => score.TeamId));

    private static async Task<string> ReadMetadataCommandAsync(string executable, IReadOnlyList<string> arguments, CancellationToken ct)
    {
        var start = new ProcessStartInfo(executable)
        {
            RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true
        };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new InvalidOperationException($"Could not start {executable} for experiment metadata.");
        var output = process.StandardOutput.ReadToEndAsync(ct);
        var error = process.StandardError.ReadToEndAsync(ct);
        await process.WaitForExitAsync(ct);
        return process.ExitCode == 0 ? (await output).Trim() : $"Unavailable (exit {process.ExitCode}): {(await error).Trim()}";
    }

    private sealed record ProjectionMeasurement(int Pair, string Reader, double ElapsedMilliseconds, long AllocatedBytes,
        int ScoreRows, int Gen0, int Gen1, int Gen2);
    private sealed record ProjectionDiagnostic(string Reader, int Queries, long ReturnedRows, long ReturnedCells,
        int ScoreRows, IReadOnlyList<ProjectionReadCommand> Commands);
}
