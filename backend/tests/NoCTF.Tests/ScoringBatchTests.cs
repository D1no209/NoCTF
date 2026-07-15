using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NoCTF.Application.Scoring;
using NoCTF.Core;
using NoCTF.Infrastructure;
using NoCTF.PluginBase;

namespace NoCTF.Tests;

public class ScoringBatchTests
{
    [Fact]
    public void SignalEmitter_RejectsDuplicateScoringStrategyKeys()
    {
        var competitionId = Guid.NewGuid();
        using var db = CreateDb(competitionId);
        var first = new CountingBatchStrategy();
        var second = new CountingBatchStrategy();

        Assert.Throws<InvalidOperationException>(() => new ScoreSignalEmitter(
            db,
            [first, second],
            new CountingProfileResolver("batch-test"),
            NullLogger<ScoreSignalEmitter>.Instance));
    }

    [Fact]
    public async Task SignalBatch_ResolvesProfileOnceAndUsesBatchStrategy()
    {
        var competitionId = Guid.NewGuid();
        await using var db = CreateDb(competitionId);
        db.Competitions.Add(CreateCompetition(competitionId));
        await db.SaveChangesAsync();
        var profile = new CountingProfileResolver("batch-test");
        var strategy = new CountingBatchStrategy();
        var emitter = new ScoreSignalEmitter(
            db,
            [strategy],
            profile,
            NullLogger<ScoreSignalEmitter>.Instance);
        var signals = Enumerable.Range(0, 8)
            .Select(index => new ScoreSignalCreate(
                competitionId,
                Guid.NewGuid(),
                "batch.test",
                $"batch:{index}"))
            .ToList();

        var emitted = await emitter.EmitBatchAsync(signals);

        Assert.Equal(8, emitted.Count);
        Assert.Equal(8, await db.ScoreSignals.IgnoreQueryFilters().CountAsync());
        Assert.Equal(1, profile.ResolveCalls);
        Assert.Equal(1, strategy.BatchCalls);
        Assert.Equal(0, strategy.SingleCalls);
        Assert.Equal(8, strategy.HandledSignals);
    }

    [Fact]
    public async Task PersistBatch_PersistsSignalsWithoutRunningIncrementalStrategies()
    {
        var competitionId = Guid.NewGuid();
        await using var db = CreateDb(competitionId);
        db.Competitions.Add(CreateCompetition(competitionId));
        await db.SaveChangesAsync();
        var profile = new CountingProfileResolver("batch-test");
        var strategy = new CountingBatchStrategy();
        var emitter = new ScoreSignalEmitter(
            db,
            [strategy],
            profile,
            NullLogger<ScoreSignalEmitter>.Instance);

        var persisted = await emitter.PersistBatchAsync([
            new ScoreSignalCreate(competitionId, Guid.NewGuid(), "batch.test", "persist:1"),
            new ScoreSignalCreate(competitionId, Guid.NewGuid(), "batch.test", "persist:2")
        ]);

        Assert.Equal(2, persisted.Count);
        Assert.Equal(2, await db.ScoreSignals.IgnoreQueryFilters().CountAsync());
        Assert.Equal(0, profile.ResolveCalls);
        Assert.Equal(0, strategy.BatchCalls);
        Assert.Equal(0, strategy.SingleCalls);
    }

    [Fact]
    public async Task EventBatch_DeduplicatesKeysAndPreservesExistingZeroDeltaSemantics()
    {
        var competitionId = Guid.NewGuid();
        await using var db = CreateDb(competitionId);
        db.Competitions.Add(CreateCompetition(competitionId));
        await db.SaveChangesAsync();
        var writer = new ScoreEventWriter(db);
        var teamId = Guid.NewGuid();
        var duplicate = new ScoreEventCreate(
            competitionId,
            teamId,
            "batch-test",
            "batch.event",
            10,
            "event:duplicate");

        var first = await writer.WriteBatchAsync([
            duplicate,
            duplicate with { PointsDelta = 20 },
            new ScoreEventCreate(
                competitionId,
                teamId,
                "batch-test",
                "batch.zero",
                0,
                "event:zero")
        ]);

        Assert.Same(first[0], first[1]);
        Assert.Null(first[2]);
        Assert.Single(await db.ScoreEvents.IgnoreQueryFilters().ToListAsync());

        var existingFromZeroDelta = await writer.WriteAsync(duplicate with { PointsDelta = 0 });
        Assert.NotNull(existingFromZeroDelta);
        Assert.Equal(first[0]!.Id, existingFromZeroDelta!.Id);
    }

    [Fact]
    public async Task DefaultBatchMethod_KeepsLegacyEmitterCompatible()
    {
        IScoreSignalEmitter emitter = new LegacySignalEmitter();
        var competitionId = Guid.NewGuid();
        var emitted = await emitter.EmitBatchAsync([
            new ScoreSignalCreate(competitionId, Guid.NewGuid(), "legacy", "legacy:1"),
            new ScoreSignalCreate(competitionId, Guid.NewGuid(), "legacy", "legacy:2")
        ]);

        Assert.Equal(2, emitted.Count);
        Assert.Equal(2, ((LegacySignalEmitter)emitter).Calls);
    }

    [Fact]
    public async Task ChallengeScopedStrategies_IgnoreTombstonedChallengeSignals()
    {
        var competitionId = Guid.NewGuid();
        var challengeId = Guid.NewGuid();
        var teamId = Guid.NewGuid();
        await using var db = CreateDb(competitionId);
        db.Competitions.Add(CreateCompetition(competitionId));
        db.Challenges.Add(new Challenge
        {
            Id = challengeId,
            CompetitionId = competitionId,
            Title = "Deleting challenge",
            TypeId = "ctf",
            PointsConfig = new PointsConfig(),
            IsDeleting = true,
            CreatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var writer = new ScoreEventWriter(db);
        var signal = new ScoreSignal
        {
            Id = Guid.NewGuid(),
            CompetitionId = competitionId,
            TeamId = teamId,
            SubjectType = "challenge",
            SubjectId = challengeId,
            OccurredAt = DateTime.UtcNow,
            IdempotencyKey = "tombstone:test",
            PayloadJson = "{}"
        };

        signal.SignalType = ScoreSignalTypes.SolveAccepted;
        await new DecaySolveScoringStrategy(db, writer).HandleAsync(signal);
        await new BloodBonusScoringStrategy(db, writer).HandleAsync(signal);

        signal.SignalType = ScoreSignalTypes.AttackAccepted;
        await new RoundAccumulationScoringStrategy(db, writer).HandleAsync(signal);

        signal.SignalType = ScoreSignalTypes.PatchVerified;
        await new OneShotVerificationScoringStrategy(db, writer).HandleAsync(signal);

        signal.SignalType = ScoreSignalTypes.ControlHeld;
        await new ControlIntervalScoringStrategy(db, writer).HandleAsync(signal);

        Assert.Empty(await db.ScoreEvents.IgnoreQueryFilters().ToListAsync());
    }

    private static ApplicationDbContext CreateDb(Guid competitionId)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSharedInMemoryServiceProvider()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new ApplicationDbContext(options, new FixedTenantContextScoringBatch(competitionId));
    }

    private static Competition CreateCompetition(Guid competitionId)
        => new()
        {
            Id = competitionId,
            CompetitionId = competitionId,
            Title = "Batch scoring",
            OwnerId = Guid.NewGuid(),
            GameModeType = GameModeType.Awd,
            ModeKey = "awd",
            StartTime = DateTime.UtcNow.AddMinutes(-1),
            EndTime = DateTime.UtcNow.AddHours(1),
            Status = CompetitionStatus.Running
        };

    private sealed class CountingProfileResolver(string scoringKey) : ICompetitionScoringProfileResolver
    {
        public int ResolveCalls { get; private set; }

        public Task<IReadOnlySet<string>> ResolveAsync(Guid competitionId, CancellationToken ct = default)
        {
            ResolveCalls++;
            return Task.FromResult<IReadOnlySet<string>>(new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                scoringKey
            });
        }
    }

    private sealed class CountingBatchStrategy : IBatchScoringStrategy
    {
        public string ScoringKey => "batch-test";
        public int SingleCalls { get; private set; }
        public int BatchCalls { get; private set; }
        public int HandledSignals { get; private set; }

        public bool CanHandle(ScoreSignal signal) => signal.SignalType == "batch.test";

        public Task HandleAsync(ScoreSignal signal, CancellationToken ct = default)
        {
            SingleCalls++;
            return Task.CompletedTask;
        }

        public Task HandleBatchAsync(IReadOnlyCollection<ScoreSignal> signals, CancellationToken ct = default)
        {
            BatchCalls++;
            HandledSignals += signals.Count;
            return Task.CompletedTask;
        }
    }

    private sealed class LegacySignalEmitter : IScoreSignalEmitter
    {
        public int Calls { get; private set; }

        public Task<ScoreSignal> EmitAsync(ScoreSignalCreate signal, CancellationToken ct = default)
        {
            Calls++;
            return Task.FromResult(new ScoreSignal
            {
                Id = Guid.NewGuid(),
                CompetitionId = signal.CompetitionId,
                TeamId = signal.TeamId,
                SignalType = signal.SignalType,
                IdempotencyKey = signal.IdempotencyKey,
                SubjectType = signal.SubjectType,
                SubjectId = signal.SubjectId,
                OccurredAt = signal.OccurredAt ?? DateTime.UtcNow,
                PayloadJson = signal.PayloadJson
            });
        }
    }
}

file sealed class FixedTenantContextScoringBatch(Guid competitionId) : ITenantContext
{
    public Guid? CompetitionId => competitionId;
    public void SetCompetitionId(Guid? id) { }
}
