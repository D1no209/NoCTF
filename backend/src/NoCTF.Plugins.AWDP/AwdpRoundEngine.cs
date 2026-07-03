using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NoCTF.Application;
using NoCTF.Core;
using NoCTF.Infrastructure;
using NoCTF.PluginBase;

namespace NoCTF.Plugins.AWDP;

public sealed class AwdpRoundEngine(IServiceProvider serviceProvider, ILogger<AwdpRoundEngine> logger)
    : BackgroundService
{
    private const int DefaultRoundDurationSeconds = 300;
    private const int DefaultTotalRounds = 10;
    private const int PollingIntervalSeconds = 5;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("AwdpRoundEngine started.");
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
                logger.LogError(ex, "AwdpRoundEngine tick failed.");
            }
        }

        logger.LogInformation("AwdpRoundEngine stopped.");
    }

    private async Task TickAllActiveCompetitionsAsync(CancellationToken ct)
    {
        await using var scope = serviceProvider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var now = DateTime.UtcNow;

        var competitions = await db.Competitions
            .IgnoreQueryFilters()
            .Where(c =>
                c.GameModeType == GameModeType.Awdp &&
                c.Status == CompetitionStatus.Running &&
                c.StartTime <= now &&
                c.EndTime > now)
            .ToListAsync(ct);

        foreach (var competition in competitions)
        {
            await TickCompetitionAsync(scope.ServiceProvider, db, competition, now, ct);
        }
    }

    private async Task TickCompetitionAsync(
        IServiceProvider scopedServices,
        ApplicationDbContext db,
        Competition competition,
        DateTime now,
        CancellationToken ct)
    {
        var roundDuration = TimeSpan.FromSeconds(competition.RoundDurationSeconds ?? DefaultRoundDurationSeconds);
        var totalRounds = competition.TotalRounds ?? DefaultTotalRounds;

        var latestRound = await db.AwdpRounds
            .IgnoreQueryFilters()
            .Where(r => r.CompetitionId == competition.Id)
            .OrderByDescending(r => r.RoundNumber)
            .FirstOrDefaultAsync(ct);

        if (latestRound is null)
        {
            await StartRoundAsync(scopedServices, db, competition, 1, totalRounds, now, ct);
            return;
        }

        if (latestRound.Status == AwdpRoundStatus.RoundRunning)
        {
            if (now - latestRound.StartTime < roundDuration)
                return;

            latestRound.Status = AwdpRoundStatus.RoundScoring;
            latestRound.EndTime = now;
            await db.SaveChangesAsync(ct);

            await scopedServices
                .GetRequiredService<AwdpScoreEngine>()
                .CalculateRoundScoreAsync(competition.Id, latestRound.RoundNumber, ct);

            latestRound.Status = AwdpRoundStatus.RoundFinished;
            latestRound.EndTime = now;
            await db.SaveChangesAsync(ct);

            if (latestRound.RoundNumber >= totalRounds)
                return;

            await StartRoundAsync(
                scopedServices,
                db,
                competition,
                latestRound.RoundNumber + 1,
                totalRounds,
                now,
                ct);
            return;
        }

        if (latestRound.Status == AwdpRoundStatus.RoundFinished &&
            latestRound.RoundNumber < totalRounds)
        {
            await StartRoundAsync(
                scopedServices,
                db,
                competition,
                latestRound.RoundNumber + 1,
                totalRounds,
                now,
                ct);
        }
    }

    private async Task StartRoundAsync(
        IServiceProvider scopedServices,
        ApplicationDbContext db,
        Competition competition,
        int roundNumber,
        int totalRounds,
        DateTime now,
        CancellationToken ct)
    {
        var newRound = new AwdpRound
        {
            Id = Guid.NewGuid(),
            CompetitionId = competition.Id,
            RoundNumber = roundNumber,
            StartTime = now,
            Status = AwdpRoundStatus.RoundRunning
        };

        db.AwdpRounds.Add(newRound);
        await db.SaveChangesAsync(ct);

        logger.LogInformation(
            "Competition {CompetitionId}: starting AWDP round {RoundNumber}/{TotalRounds}.",
            competition.Id,
            roundNumber,
            totalRounds);

        var hubNotifier = scopedServices.GetRequiredService<IHubNotifierService>();
        await hubNotifier.NotifyRoundStartedAsync(competition.Id, roundNumber, ct);
    }
}
