using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NoCTF.Application;
using NoCTF.Application.BackgroundTasks;
using NoCTF.Core;
using NoCTF.Infrastructure;
using NoCTF.PluginBase;

namespace NoCTF.Plugins.AWD;

/// <summary>
/// Background service that drives AWD round ticks for all active AWD competitions.
/// Polls every 5 seconds; when a competition's round duration elapses, advances to the next round.
/// </summary>
public sealed class AwdRoundEngine : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<AwdRoundEngine> _logger;

    // Default values used when Competition.RoundDurationSeconds / TotalRounds are null.
    private const int DefaultRoundDurationSeconds = 300;
    private const int DefaultTotalRounds = 10;
    private const int PollingIntervalSeconds = 5;

    public AwdRoundEngine(IServiceProvider serviceProvider, ILogger<AwdRoundEngine> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("AwdRoundEngine started.");

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(PollingIntervalSeconds));

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await TickAllActiveCompetitionsAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "AwdRoundEngine tick failed.");
            }
        }

        _logger.LogInformation("AwdRoundEngine stopped.");
    }

    private async Task TickAllActiveCompetitionsAsync(CancellationToken ct)
    {
        await using var scope = _serviceProvider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var hubNotifier = scope.ServiceProvider.GetRequiredService<IHubNotifierService>();
        var executionLease = scope.ServiceProvider.GetRequiredService<ICompetitionExecutionLease>();

        var now = DateTime.UtcNow;

        // Find all active AWD competitions (ignore tenant filter — engine is global)
        var competitions = await db.Competitions
            .IgnoreQueryFilters()
            .Where(c => c.GameModeType == GameModeType.Awd &&
                        c.Status == CompetitionStatus.Running &&
                        c.StartTime <= now)
            .ToListAsync(ct);

        foreach (var competition in competitions)
        {
            await using var lease = await executionLease.TryAcquireAsync(db, "awd-round", competition.Id, ct);
            if (lease is null)
                continue;

            using var leaseCts = CancellationTokenSource.CreateLinkedTokenSource(ct, lease.LostToken);
            await TickCompetitionAsync(scope.ServiceProvider, db, hubNotifier, competition, now, leaseCts.Token);
            if (db.ChangeTracker.HasChanges())
                await db.SaveChangesAsync(leaseCts.Token);
        }
    }

    internal async Task TickCompetitionAsync(
        IServiceProvider scopedServices,
        ApplicationDbContext db,
        IHubNotifierService hubNotifier,
        Competition competition,
        DateTime now,
        CancellationToken ct)
    {
        var roundDuration = TimeSpan.FromSeconds(
            competition.RoundDurationSeconds ?? DefaultRoundDurationSeconds);
        var totalRounds = competition.TotalRounds ?? DefaultTotalRounds;

        // Find the latest round for this competition
        var latestRound = await db.AwdRounds
            .IgnoreQueryFilters()
            .Where(r => r.CompetitionId == competition.Id)
            .OrderByDescending(r => r.RoundNumber)
            .FirstOrDefaultAsync(ct);

        // Ended competitions must remain discoverable after a process outage;
        // otherwise the EndTime filter leaves both the round and competition in
        // Running forever when no tick occurred before the deadline.
        if (now >= competition.EndTime)
        {
            if (latestRound is not null && latestRound.Status != AwdRoundStatus.Finished)
            {
                latestRound.Status = AwdRoundStatus.Finished;
                latestRound.EndTime = competition.EndTime;
            }

            competition.Status = CompetitionStatus.Finished;
            await db.SaveChangesAsync(ct);
            return;
        }

        // A running round can already be durable while its external preparation
        // was interrupted by a process crash. Resume that idempotent preparation
        // before deciding whether the round may advance.
        if (latestRound?.Status == AwdRoundStatus.Running &&
            !await IsRoundPreparedAsync(db, competition.Id, latestRound.RoundNumber, ct))
        {
            if (!await PrepareRoundAsync(
                    scopedServices,
                    db,
                    hubNotifier,
                    competition,
                    latestRound.RoundNumber,
                    totalRounds,
                    ct))
            {
                return;
            }
        }

        // Determine next round number
        int nextRoundNumber;
        if (latestRound is null)
        {
            // No rounds yet — start round 1
            nextRoundNumber = 1;
        }
        else if (latestRound.Status == AwdRoundStatus.Running)
        {
            // Current round still in progress — check if it should end
            if (now - latestRound.StartTime < roundDuration)
                return; // Not time yet

            // End the current round
            latestRound.EndTime = now;
            latestRound.Status = AwdRoundStatus.Finished;

            if (latestRound.RoundNumber >= totalRounds)
            {
                _logger.LogInformation(
                    "Competition {CompetitionId} finished all {TotalRounds} rounds.",
                    competition.Id, totalRounds);
                competition.Status = CompetitionStatus.Finished;
                return;
            }

            nextRoundNumber = latestRound.RoundNumber + 1;
        }
        else if (latestRound.Status == AwdRoundStatus.Finished)
        {
            if (latestRound.RoundNumber >= totalRounds)
            {
                competition.Status = CompetitionStatus.Finished;
                return; // All rounds done
            }

            nextRoundNumber = latestRound.RoundNumber + 1;
        }
        else
        {
            // Paused or Idle — do not advance
            return;
        }

        // Start the new round
        var newRound = new AwdRound
        {
            Id = Guid.NewGuid(),
            CompetitionId = competition.Id,
            RoundNumber = nextRoundNumber,
            StartTime = now,
            Status = AwdRoundStatus.Running
        };

        db.AwdRounds.Add(newRound);
        // Persist the phase transition before crossing the notification/Runner
        // boundary. A restarted engine can then discover and finish preparation.
        await db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Competition {CompetitionId}: starting round {RoundNumber}/{TotalRounds}.",
            competition.Id, nextRoundNumber, totalRounds);

        await PrepareRoundAsync(
            scopedServices,
            db,
            hubNotifier,
            competition,
            nextRoundNumber,
            totalRounds,
            ct);
    }

    private async Task<bool> PrepareRoundAsync(
        IServiceProvider scopedServices,
        ApplicationDbContext db,
        IHubNotifierService hubNotifier,
        Competition competition,
        int roundNumber,
        int totalRounds,
        CancellationToken ct)
    {
        try
        {
            // Stream delivery is at-least-once. Replaying the same round after a
            // crash is preferable to permanently missing the round transition.
            try
            {
                await hubNotifier.NotifyRoundStartedAsync(competition.Id, roundNumber, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "Competition {CompetitionId}: round {Round} notification failed.",
                    competition.Id,
                    roundNumber);
            }

            // Every operation below has a deterministic database/runtime key,
            // so an interrupted phase can safely be replayed by the next tick.
            var flagService = scopedServices.GetRequiredService<AwdFlagService>();
            await flagService.GenerateRoundFlagsAsync(competition.Id, roundNumber, ct);
            if (!await flagService.RefreshRoundFlagsAsync(competition.Id, roundNumber, ct))
            {
                _logger.LogWarning(
                    "Competition {CompetitionId}: round {Round} runtime refresh is incomplete; it will be retried.",
                    competition.Id,
                    roundNumber);
                return false;
            }

            await scopedServices
                .GetRequiredService<AwdCheckerService>()
                .RunCheckerAsync(competition.Id, roundNumber, ct);
            await scopedServices
                .GetRequiredService<AwdScoreEngine>()
                .CalculateRoundScoreAsync(competition.Id, roundNumber, ct);

            var checkpoint = new CompetitionEngineState
            {
                Id = Guid.NewGuid(),
                CompetitionId = competition.Id,
                EngineKey = PreparationEngineKey(roundNumber),
                LastExecutedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            db.CompetitionEngineStates.Add(checkpoint);
            await db.SaveChangesAsync(ct);

            _logger.LogInformation(
                "Competition {CompetitionId}: AWD round {Round}/{TotalRounds} preparation completed.",
                competition.Id,
                roundNumber,
                totalRounds);
            return true;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (DbUpdateException ex)
        {
            foreach (var entry in db.ChangeTracker.Entries<CompetitionEngineState>()
                         .Where(entry =>
                             entry.State == EntityState.Added &&
                             entry.Entity.CompetitionId == competition.Id &&
                             entry.Entity.EngineKey == PreparationEngineKey(roundNumber)))
            {
                entry.State = EntityState.Detached;
            }

            if (await IsRoundPreparedAsync(db, competition.Id, roundNumber, ct))
            {
                // A unique checkpoint written by a concurrent/recovered
                // execution proves that preparation completed.
                return true;
            }

            _logger.LogError(
                ex,
                "Competition {CompetitionId}: AWD round {Round} checkpoint failed; it will be retried.",
                competition.Id,
                roundNumber);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Competition {CompetitionId}: AWD round {Round} preparation failed; it will be retried.",
                competition.Id,
                roundNumber);
            return false;
        }
    }

    private static Task<bool> IsRoundPreparedAsync(
        ApplicationDbContext db,
        Guid competitionId,
        int roundNumber,
        CancellationToken ct)
        => db.CompetitionEngineStates
            .IgnoreQueryFilters()
            .AsNoTracking()
            .AnyAsync(state =>
                state.CompetitionId == competitionId &&
                state.EngineKey == PreparationEngineKey(roundNumber),
                ct);

    private static string PreparationEngineKey(int roundNumber)
        => $"awd-round:{roundNumber}:prepared";
}
