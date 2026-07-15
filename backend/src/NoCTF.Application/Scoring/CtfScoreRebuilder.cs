using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using NoCTF.Core;
using NoCTF.Infrastructure;
using NoCTF.PluginBase;
using NoCTF.Application.CompetitionModes;

namespace NoCTF.Application.Scoring;

public sealed class CtfScoreRebuilder(
    ApplicationDbContext db,
    IEnumerable<IChallengeSubmissionHandler>? customSubmissionHandlers = null,
    IEnumerable<IScoreRebuildContributor>? contributors = null) : ICtfScoreRebuilder
{
    private sealed record RankedSolve(Guid TeamId, DateTime SubmittedAt, int Rank, Guid? SignalId);
    private readonly IChallengeSubmissionHandler[] _customSubmissionHandlers =
        ProviderRegistry.BuildUnique(
                customSubmissionHandlers ?? [],
                handler => handler.TypeId,
                "challenge submission")
            .Values
            .ToArray();
    private readonly IScoreRebuildContributor[] _contributors = ValidateContributors(contributors ?? []);

    public async Task RebuildCompetitionAsync(Guid competitionId, CancellationToken ct = default)
    {
        var challengeIds = await db.Challenges
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(c => c.CompetitionId == competitionId && !c.IsDeleting)
            .Select(c => c.Id)
            .ToListAsync(ct);

        await RebuildAsync(competitionId, challengeIds, removeAllCompetitionCtfEvents: true, ct);
    }

    public Task RebuildChallengeAsync(Guid competitionId, Guid challengeId, CancellationToken ct = default)
        => RebuildAsync(competitionId, [challengeId], removeAllCompetitionCtfEvents: false, ct);

    private async Task RebuildAsync(
        Guid competitionId,
        IReadOnlyCollection<Guid> challengeIds,
        bool removeAllCompetitionCtfEvents,
        CancellationToken ct)
    {
        if (challengeIds.Count == 0 && !removeAllCompetitionCtfEvents)
            return;

        await using var rebuildLock = await CtfScoreRebuildLock.AcquireAsync(db, competitionId, challengeIds, ct);

        var competition = await db.Competitions
            .IgnoreQueryFilters()
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == competitionId, ct);
        if (competition is null ||
            (competition.GameModeType != GameModeType.Ctf &&
             !string.Equals(competition.ModeKey, "ctf", StringComparison.OrdinalIgnoreCase)))
        {
            await rebuildLock.CommitAsync(ct);
            return;
        }

        var allChallenges = await db.Challenges
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(c =>
                c.CompetitionId == competitionId &&
                challengeIds.Contains(c.Id) &&
                !c.IsDeleting)
            .OrderBy(c => c.CreatedAt)
            .ToListAsync(ct);
        if (allChallenges.Count == 0 && !removeAllCompetitionCtfEvents)
        {
            await rebuildLock.CommitAsync(ct);
            return;
        }

        var activeChallengeIds = allChallenges.Select(c => c.Id).ToHashSet();
        var customTypeIds = _customSubmissionHandlers
            .Select(handler => handler.TypeId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var contributor in _contributors)
        {
            foreach (var typeId in contributor.OwnedChallengeTypeIds)
                customTypeIds.Add(typeId);
        }
        var challenges = allChallenges
            .Where(challenge => !customTypeIds.Contains(challenge.TypeId))
            .ToList();
        var staleQuery = db.ScoreEvents
            .IgnoreQueryFilters()
            .Where(e =>
                e.CompetitionId == competitionId &&
                (e.ScoringKey == ScoringKeys.DecaySolve || e.ScoringKey == ScoringKeys.BloodBonus));
        if (!removeAllCompetitionCtfEvents)
        {
            staleQuery = staleQuery.Where(e =>
                e.ChallengeId.HasValue &&
                activeChallengeIds.Contains(e.ChallengeId.Value));
        }

        if (db.Database.IsRelational())
        {
            await staleQuery.ExecuteDeleteAsync(ct);
        }
        else
        {
            var staleEvents = await staleQuery.ToListAsync(ct);
            db.ScoreEvents.RemoveRange(staleEvents);
            await db.SaveChangesAsync(ct);
        }

        var events = new List<ScoreEvent>();
        var rankedSolvesByChallenge = await GetRankedSolvesAsync(
            competitionId,
            challenges.Select(challenge => challenge.Id).ToArray(),
            ct);
        foreach (var challenge in challenges)
        {
            if (!rankedSolvesByChallenge.TryGetValue(challenge.Id, out var solves) || solves.Count == 0)
                continue;

            var currentPoints = CtfScoreCalculator.CalculateChallengePoints(
                solves.Count,
                challenge.PointsConfig,
                challenge.DifficultyCoefficient);

            foreach (var solve in solves)
            {
                events.Add(new ScoreEvent
                {
                    Id = Guid.NewGuid(),
                    CompetitionId = competitionId,
                    TeamId = solve.TeamId,
                    ChallengeId = challenge.Id,
                    ScoringKey = ScoringKeys.DecaySolve,
                    EventType = "ctf.solve.rebuilt",
                    PointsDelta = currentPoints,
                    Reason = $"Rebuilt solve score for challenge '{challenge.Title}'",
                    Timestamp = solve.SubmittedAt,
                    SourceSignalId = solve.SignalId,
                    IdempotencyKey = $"ctf-rebuild:{solve.TeamId:N}:{challenge.Id:N}:decay:v1",
                    MetadataJson = ScoringJson.Serialize(new
                    {
                        solveCount = solves.Count,
                        currentPoints,
                        solveRank = solve.Rank,
                        rebuilt = true,
                        decayFunction = challenge.PointsConfig.DecayFunction,
                        difficultyCoefficient = challenge.DifficultyCoefficient
                    })
                });

                if (solve.Rank > 3)
                    continue;

                var bonus = CtfScoreCalculator.CalculateBloodBonus(
                    solve.Rank,
                    currentPoints,
                    competition,
                    challenge.EnableBloodBonus);
                if (bonus == 0)
                    continue;

                events.Add(new ScoreEvent
                {
                    Id = Guid.NewGuid(),
                    CompetitionId = competitionId,
                    TeamId = solve.TeamId,
                    ChallengeId = challenge.Id,
                    ScoringKey = ScoringKeys.BloodBonus,
                    EventType = "ctf.blood-bonus.rebuilt",
                    PointsDelta = bonus,
                    Reason = solve.Rank switch
                    {
                        1 => $"Rebuilt first blood bonus for challenge '{challenge.Title}'",
                        2 => $"Rebuilt second blood bonus for challenge '{challenge.Title}'",
                        _ => $"Rebuilt third blood bonus for challenge '{challenge.Title}'"
                    },
                    Timestamp = solve.SubmittedAt,
                    SourceSignalId = solve.SignalId,
                    IdempotencyKey = $"ctf-rebuild:{solve.TeamId:N}:{challenge.Id:N}:blood:{solve.Rank}:v1",
                    MetadataJson = ScoringJson.Serialize(new
                    {
                        solveCount = solves.Count,
                        currentPoints,
                        bonusPoints = bonus,
                        solveRank = solve.Rank,
                        rebuilt = true
                    })
                });
            }
        }

        db.ScoreEvents.AddRange(events);
        await db.SaveChangesAsync(ct);

        foreach (var contributor in _contributors)
        {
            await contributor.RebuildAsync(
                competition,
                activeChallengeIds,
                removeAllCompetitionCtfEvents,
                ct);
        }

        await rebuildLock.CommitAsync(ct);
    }

    private static IScoreRebuildContributor[] ValidateContributors(
        IEnumerable<IScoreRebuildContributor> contributors)
    {
        var result = contributors.ToArray();
        var owners = new Dictionary<string, IScoreRebuildContributor>(StringComparer.OrdinalIgnoreCase);
        foreach (var contributor in result)
        {
            foreach (var configuredTypeId in contributor.OwnedChallengeTypeIds)
            {
                var typeId = configuredTypeId?.Trim();
                if (string.IsNullOrWhiteSpace(typeId))
                    throw new InvalidOperationException("A score rebuild contributor owns an empty challenge type.");
                if (!owners.TryAdd(typeId, contributor))
                {
                    throw new InvalidOperationException(
                        $"Multiple score rebuild contributors own challenge type '{typeId}'.");
                }
            }
        }

        return result;
    }

    private async Task<Dictionary<Guid, List<RankedSolve>>> GetRankedSolvesAsync(
        Guid competitionId,
        IReadOnlyCollection<Guid> challengeIds,
        CancellationToken ct)
    {
        if (challengeIds.Count == 0)
            return [];

        var submissions = await db.Submissions
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(s =>
                s.CompetitionId == competitionId &&
                challengeIds.Contains(s.ChallengeId) &&
                s.IsCorrect)
            .Join(
                db.Teams.IgnoreQueryFilters().AsNoTracking().Where(t =>
                    t.CompetitionId == competitionId &&
                    t.RegistrationStatus == TeamRegistrationStatus.Approved &&
                    !t.IsBanned),
                s => s.TeamId,
                t => t.Id,
                (s, _) => new { s.ChallengeId, s.TeamId, s.SubmittedAt })
            .GroupBy(s => new { s.ChallengeId, s.TeamId })
            .Select(g => new
            {
                g.Key.ChallengeId,
                g.Key.TeamId,
                SubmittedAt = g.Min(s => s.SubmittedAt)
            })
            .ToListAsync(ct);

        if (submissions.Count == 0)
            return [];

        var teamIds = submissions.Select(s => s.TeamId).ToHashSet();
        var signalRows = await db.ScoreSignals
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(s =>
                s.CompetitionId == competitionId &&
                s.SubjectId.HasValue &&
                challengeIds.Contains(s.SubjectId.Value) &&
                s.SignalType == ScoreSignalTypes.SolveAccepted &&
                teamIds.Contains(s.TeamId))
            .GroupBy(s => new { ChallengeId = s.SubjectId!.Value, s.TeamId })
            .Select(g => new
            {
                g.Key.ChallengeId,
                g.Key.TeamId,
                SignalId = g.OrderBy(s => s.OccurredAt).Select(s => s.Id).FirstOrDefault()
            })
            .ToListAsync(ct);
        var signalMap = signalRows.ToDictionary(
            signal => (signal.ChallengeId, signal.TeamId),
            signal => (Guid?)signal.SignalId);

        return submissions
            .GroupBy(solve => solve.ChallengeId)
            .ToDictionary(
                group => group.Key,
                group => group
                    .OrderBy(solve => solve.SubmittedAt)
                    .Select((solve, index) => new RankedSolve(
                        solve.TeamId,
                        solve.SubmittedAt,
                        index + 1,
                        signalMap.GetValueOrDefault((solve.ChallengeId, solve.TeamId))))
                    .ToList());
    }
}

internal sealed class CtfScoreRebuildLock : IAsyncDisposable
{
    private readonly IDbContextTransaction? _transaction;
    private bool _committed;

    private CtfScoreRebuildLock(IDbContextTransaction? transaction)
    {
        _transaction = transaction;
    }

    public static async Task<CtfScoreRebuildLock> AcquireAsync(
        ApplicationDbContext db,
        Guid competitionId,
        IReadOnlyCollection<Guid> challengeIds,
        CancellationToken ct)
    {
        if (!db.Database.IsRelational())
            return new CtfScoreRebuildLock(null);

        // Destructive workflows may already own a serializable transaction.
        // Participate in it instead of attempting an illegal nested EF
        // transaction; only a transaction created here is committed/disposed by
        // this lock wrapper.
        if (db.Database.CurrentTransaction is not null)
        {
            await AcquirePostgresAdvisoryLockAsync(db, competitionId, ct);
            return new CtfScoreRebuildLock(null);
        }

        var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        try
        {
            await AcquirePostgresAdvisoryLockAsync(db, competitionId, ct);
            return new CtfScoreRebuildLock(transaction);
        }
        catch
        {
            await transaction.DisposeAsync();
            throw;
        }
    }

    private static async Task AcquirePostgresAdvisoryLockAsync(
        ApplicationDbContext db,
        Guid competitionId,
        CancellationToken ct)
    {
        if (db.Database.ProviderName?.Contains("Npgsql", StringComparison.OrdinalIgnoreCase) != true)
            return;

        var lockKey = AdvisoryLockKey(competitionId, Guid.Empty);
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock({lockKey})",
            ct);
    }

    public async Task CommitAsync(CancellationToken ct)
    {
        if (_transaction is null || _committed)
            return;

        await _transaction.CommitAsync(ct);
        _committed = true;
    }

    public async ValueTask DisposeAsync()
    {
        if (_transaction is not null)
            await _transaction.DisposeAsync();
    }

    private static long AdvisoryLockKey(Guid competitionId, Guid challengeId)
    {
        var competitionPart = BitConverter.ToInt32(competitionId.ToByteArray(), 0);
        var challengePart = BitConverter.ToInt32(challengeId.ToByteArray(), 0);
        return ((long)competitionPart << 32) ^ (uint)challengePart;
    }
}
