using System.Diagnostics;
using System.Data.Common;
using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using NoCTF.Application.Challenges.Bank;
using NoCTF.Domain.Challenges;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Identity;
using NoCTF.Infrastructure.Challenges.Bank;
using NoCTF.Infrastructure.Persistence;
using Testcontainers.PostgreSql;

namespace NoCTF.Tests.Integration.Persistence;

[Category("Integration")]
[Category("Performance")]
public sealed class ChallengeReadPerformanceTests
{
    [Test]
    [Timeout(300_000)]
    public async Task Summary_page_is_measured_against_the_full_graph_projection(
        CancellationToken cancellationToken)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var postgres = new PostgreSqlBuilder("postgres:17.10-alpine3.24@sha256:742f40ea20b9ff2ff31db5458d127452988a2164df9e17441e191f3b72252193")
                .WithDatabase("noctf_challenge_read_performance")
                .WithUsername("postgres")
                .WithPassword("postgres")
                .Build();
            await postgres.StartAsync(cancellationToken);
            var commands = new CommandCapture();
            var options = new DbContextOptionsBuilder<NoCtfDbContext>()
                .UseNpgsql(postgres.GetConnectionString())
                .UseSnakeCaseNamingConvention()
                .AddInterceptors(commands)
                .Options;
            var (ownerId, challengeId) = await SeedAsync(options, cancellationToken);

            await FullReadAsync(options, cancellationToken);
            commands.Texts.Clear();
            await SummaryReadAsync(options, ownerId, cancellationToken);
            var summaryCommands = commands.Texts.ToArray();
            await Assert.That(summaryCommands.Length).IsGreaterThan(0);
            await Assert.That(summaryCommands.Any(command => command.Contains("description", StringComparison.OrdinalIgnoreCase))).IsFalse();
            await Assert.That(summaryCommands.Any(command => command.Contains("runtime_services", StringComparison.OrdinalIgnoreCase))).IsFalse();
            await FullDetailReadAsync(options, challengeId, cancellationToken);
            await SplitDetailReadAsync(options, ownerId, challengeId, cancellationToken);
            var fullSamples = new double[10];
            var summarySamples = new double[10];
            var fullDetailSamples = new double[10];
            var splitDetailSamples = new double[10];
            for (var iteration = 0; iteration < fullSamples.Length; iteration++)
            {
                fullSamples[iteration] = await MeasureAsync(
                    () => FullReadAsync(options, cancellationToken));
                summarySamples[iteration] = await MeasureAsync(
                    () => SummaryReadAsync(options, ownerId, cancellationToken));
                fullDetailSamples[iteration] = await MeasureAsync(
                    () => FullDetailReadAsync(options, challengeId, cancellationToken));
                splitDetailSamples[iteration] = await MeasureAsync(
                    () => SplitDetailReadAsync(options, ownerId, challengeId, cancellationToken));
            }

            var artifactDirectory = Path.Combine(Environment.CurrentDirectory, "TestResults", "performance");
            Directory.CreateDirectory(artifactDirectory);
            await File.WriteAllLinesAsync(
                Path.Combine(artifactDirectory, "challenge-read-current.csv"),
                ["iteration,full_ms,summary_ms,full_detail_ms,split_detail_ms", .. fullSamples.Select((full, index) =>
                    $"{index + 1},{full.ToString(CultureInfo.InvariantCulture)},{summarySamples[index].ToString(CultureInfo.InvariantCulture)},{fullDetailSamples[index].ToString(CultureInfo.InvariantCulture)},{splitDetailSamples[index].ToString(CultureInfo.InvariantCulture)}")],
                cancellationToken);

            Console.WriteLine(
                $"ChallengeReadPerformance full p50={Percentile(fullSamples, 0.5):F2}ms p95={Percentile(fullSamples, 0.95):F2}ms; "
                + $"summary p50={Percentile(summarySamples, 0.5):F2}ms p95={Percentile(summarySamples, 0.95):F2}ms");
            Console.WriteLine(
                $"ChallengeDetailPerformance full p50={Percentile(fullDetailSamples, 0.5):F2}ms p95={Percentile(fullDetailSamples, 0.95):F2}ms; "
                + $"split p50={Percentile(splitDetailSamples, 0.5):F2}ms p95={Percentile(splitDetailSamples, 0.95):F2}ms");
            // Report timings for controlled benchmark comparisons. Hosted CI scheduling can
            // reverse millisecond p95 samples; the query and result assertions above gate CI.
        });
    }

    private static async Task<(Guid OwnerId, Guid ChallengeId)> SeedAsync(
        DbContextOptions<NoCtfDbContext> options,
        CancellationToken ct)
    {
        await using var db = new NoCtfDbContext(options);
        await db.Database.EnsureCreatedAsync(ct);
        var now = DateTimeOffset.UtcNow;
        var ownerId = Guid.CreateVersion7();
        db.Users.Add(new User
        {
            Id = ownerId,
            UserName = "performance-owner",
            NormalizedUserName = "PERFORMANCE-OWNER",
            Email = "performance-owner@example.test",
            PasswordHash = "test",
            Kind = UserKind.Human,
            Role = UserRole.Administrator,
            EmailVerifiedAt = now,
            CreatedAt = now,
            UpdatedAt = now
        });
        Guid firstChallengeId = default;
        for (var index = 0; index < 55; index++)
        {
            var challengeId = Guid.CreateVersion7();
            if (index == 0) firstChallengeId = challengeId;
            db.Challenges.Add(new CtfChallenge
            {
                Id = challengeId,
                OwnerId = ownerId,
                Visibility = ChallengeVisibility.Shared,
                Title = $"Performance challenge {index:D2}",
                Description = new string('x', 512),
                Direction = "Web",
                Definition = RichDefinition(),
                CreatedAt = now,
                UpdatedAt = now.AddMinutes(index)
            });
        }
        await db.SaveChangesAsync(ct);
        return (ownerId, firstChallengeId);
    }

    private static CtfChallengeDefinition RichDefinition() => new()
    {
        Runtime = new ContainerChallengeRuntimeTemplate
        {
            Services = [new ChallengeRuntimeService { Name = "main", Image = "registry.example.test/challenge:current" }],
            Allocation = PersistedRuntimeAllocation.PerTeam,
            FlagSource = PersistedRuntimeFlagSource.PerTeam,
            UrlBindings = [
                new() { Position = 0, UrlTemplate = "http://{HOST}:{PORT}", ContainerPort = 80 },
                new() { Position = 1, UrlTemplate = "tcp://{HOST}:{PORT}", ContainerPort = 443 }
            ],
        }
    };

    private static async Task FullReadAsync(
        DbContextOptions<NoCtfDbContext> options,
        CancellationToken ct)
    {
        await using var db = new NoCtfDbContext(options);
        var items = await db.Challenges.AsNoTracking()
            .OrderBy(challenge => challenge.UpdatedAt)
            .ThenBy(challenge => challenge.Id)
            .Take(10)
            .Select(challenge => new ChallengeTemplateView(
                challenge.Id, challenge.OwnerId,
                challenge.Managers.Select(manager => manager.UserId).ToArray(),
                challenge.Mode, challenge.Visibility, challenge.Title,
                challenge.Description, challenge.Direction, challenge.Definition!,
                challenge.DeletedAt, 0, challenge.CreatedAt, challenge.UpdatedAt))
            .ToArrayAsync(ct);
        if (items.Length != 10) throw new InvalidOperationException("Unexpected full page size.");
    }

    private static async Task SummaryReadAsync(
        DbContextOptions<NoCtfDbContext> options,
        Guid ownerId,
        CancellationToken ct)
    {
        await using var db = new NoCtfDbContext(options);
        var page = await new ChallengeBankStore(db).ListPageAsync(
            new(ownerId, true, false, null, null, 0, 10, false), ct);
        if (page.Items.Count != 10 || page.Total != 55)
            throw new InvalidOperationException("Unexpected summary page size.");
    }

    private static async Task FullDetailReadAsync(
        DbContextOptions<NoCtfDbContext> options,
        Guid challengeId,
        CancellationToken ct)
    {
        await using var db = new NoCtfDbContext(options);
        var item = await db.Challenges.AsNoTracking()
            .Where(challenge => challenge.Id == challengeId)
            .Select(challenge => new ChallengeTemplateView(
                challenge.Id, challenge.OwnerId,
                challenge.Managers.Select(manager => manager.UserId).ToArray(),
                challenge.Mode, challenge.Visibility, challenge.Title,
                challenge.Description, challenge.Direction, challenge.Definition!,
                challenge.DeletedAt, 0, challenge.CreatedAt, challenge.UpdatedAt))
            .SingleAsync(ct);
        if (item.Definition.Runtime is null)
            throw new InvalidOperationException("The detail Runtime was not loaded.");
    }

    private static async Task SplitDetailReadAsync(
        DbContextOptions<NoCtfDbContext> options,
        Guid ownerId,
        Guid challengeId,
        CancellationToken ct)
    {
        await using var db = new NoCtfDbContext(options);
        var item = await new ChallengeBankStore(db).FindAsync(
            challengeId, ownerId, true, false, ct);
        if (item?.Definition.Runtime is null)
            throw new InvalidOperationException("The split detail Runtime was not loaded.");
    }

    private static async Task<double> MeasureAsync(Func<Task> action)
    {
        var started = Stopwatch.GetTimestamp();
        await action();
        return Stopwatch.GetElapsedTime(started).TotalMilliseconds;
    }

    private static double Percentile(double[] samples, double percentile)
    {
        var ordered = samples.Order().ToArray();
        return ordered[(int)Math.Ceiling(percentile * ordered.Length) - 1];
    }

    private sealed class CommandCapture : DbCommandInterceptor
    {
        public List<string> Texts { get; } = [];

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            Texts.Add(command.CommandText);
            return ValueTask.FromResult(result);
        }
    }
}
