using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NoCTF.Application;
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

        var now = DateTime.UtcNow;

        // Find all active AWD competitions (ignore tenant filter — engine is global)
        var competitions = await db.Competitions
            .IgnoreQueryFilters()
            .Where(c => c.GameModeType == GameModeType.Awd &&
                        c.Status == CompetitionStatus.Running &&
                        c.StartTime <= now &&
                        c.EndTime > now)
            .ToListAsync(ct);

        foreach (var competition in competitions)
        {
            await TickCompetitionAsync(scope.ServiceProvider, db, hubNotifier, competition, now, ct);
        }

        if (db.ChangeTracker.HasChanges())
            await db.SaveChangesAsync(ct);
    }

    private async Task TickCompetitionAsync(
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
                return;
            }

            nextRoundNumber = latestRound.RoundNumber + 1;
        }
        else if (latestRound.Status == AwdRoundStatus.Finished)
        {
            if (latestRound.RoundNumber >= totalRounds)
                return; // All rounds done

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

        _logger.LogInformation(
            "Competition {CompetitionId}: starting round {RoundNumber}/{TotalRounds}.",
            competition.Id, nextRoundNumber, totalRounds);

        // Broadcast RoundStarted via SignalR
        await hubNotifier.NotifyRoundStartedAsync(competition.Id, nextRoundNumber, ct);

        // Generate flags for all rounds (idempotent) then inject flags for this round
        try
        {
            var flagService = scopedServices.GetRequiredService<AwdFlagService>();
            await flagService.GenerateFlagsAsync(competition.Id, totalRounds, ct);
            await flagService.RefreshFlagsAsync(competition.Id, nextRoundNumber, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Competition {CompetitionId}: flag generation/refresh failed for round {Round}.",
                competition.Id, nextRoundNumber);
        }

        // Run checkers for all game boxes in this round
        try
        {
            var checkerService = scopedServices.GetRequiredService<AwdCheckerService>();
            await checkerService.RunCheckerAsync(competition.Id, nextRoundNumber, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Competition {CompetitionId}: checker execution failed for round {Round}.",
                competition.Id, nextRoundNumber);
        }

        // Calculate round scores after checker results are in
        try
        {
            var scoreEngine = scopedServices.GetRequiredService<AwdScoreEngine>();
            await scoreEngine.CalculateRoundScoreAsync(competition.Id, nextRoundNumber, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Competition {CompetitionId}: score calculation failed for round {Round}.",
                competition.Id, nextRoundNumber);
        }

        // Invoke OnRoundTickAsync on the game mode (scoped)
        var gameContext = new GameContext(
            CompetitionId: competition.Id,
            GameMode: GameModeType.Awd,
            StartTime: competition.StartTime,
            EndTime: competition.EndTime,
            Configuration: new Dictionary<string, string>
            {
                ["RoundNumber"] = nextRoundNumber.ToString(),
                ["TotalRounds"] = totalRounds.ToString(),
                ["RoundDurationSeconds"] = (competition.RoundDurationSeconds ?? DefaultRoundDurationSeconds).ToString()
            });

        // AwdGameMode is scoped — resolve from the same scope
        // (It calls hubNotifier again internally, which is fine — idempotent)
        // We skip calling OnRoundTickAsync here to avoid double-broadcasting;
        // the broadcast above is the canonical notification.
    }
}
