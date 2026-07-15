using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NoCTF.Application;
using NoCTF.Application.BackgroundTasks;
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
        var executionLease = scope.ServiceProvider.GetRequiredService<ICompetitionExecutionLease>();
        var now = DateTime.UtcNow;

        var competitions = await db.Competitions
            .IgnoreQueryFilters()
            .Where(c =>
                c.GameModeType == GameModeType.Awdp &&
                c.Status == CompetitionStatus.Running &&
                c.StartTime <= now)
            .ToListAsync(ct);

        foreach (var competition in competitions)
        {
            await using var lease = await executionLease.TryAcquireAsync(db, "awdp-round", competition.Id, ct);
            if (lease is null)
                continue;

            using var leaseCts = CancellationTokenSource.CreateLinkedTokenSource(ct, lease.LostToken);
            await TickCompetitionAsync(scope.ServiceProvider, db, competition, now, leaseCts.Token);
        }
    }

    internal async Task TickCompetitionAsync(
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

        // Keep ended competitions schedulable until their durable scoring phase
        // has completed. A host outage across EndTime must not strand a round in
        // Running/Scoring forever.
        if (now >= competition.EndTime)
        {
            if (latestRound is null)
            {
                competition.Status = CompetitionStatus.Finished;
                await db.SaveChangesAsync(ct);
                return;
            }

            if (latestRound.Status == AwdpRoundStatus.RoundRunning)
            {
                latestRound.Status = AwdpRoundStatus.RoundScoring;
                latestRound.EndTime = competition.EndTime;
                await db.SaveChangesAsync(ct);
            }

            if (latestRound.Status == AwdpRoundStatus.RoundScoring)
            {
                await CompleteScoringAsync(
                    scopedServices,
                    db,
                    competition,
                    latestRound,
                    totalRounds,
                    competition.EndTime,
                    ct,
                    startNextRound: false);
                return;
            }

            if (latestRound.Status == AwdpRoundStatus.RoundPending)
            {
                latestRound.Status = AwdpRoundStatus.RoundFinished;
                latestRound.EndTime = competition.EndTime;
            }
            else if (latestRound.Status == AwdpRoundStatus.RoundFinished)
            {
                latestRound.EndTime ??= competition.EndTime;
            }
            competition.Status = CompetitionStatus.Finished;
            await db.SaveChangesAsync(ct);
            return;
        }

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
        }

        if (latestRound.Status == AwdpRoundStatus.RoundScoring)
        {
            await CompleteScoringAsync(
                scopedServices,
                db,
                competition,
                latestRound,
                totalRounds,
                now,
                ct);
            return;
        }

        if (latestRound.Status == AwdpRoundStatus.RoundFinished)
        {
            if (latestRound.RoundNumber >= totalRounds)
            {
                if (competition.Status != CompetitionStatus.Finished)
                {
                    competition.Status = CompetitionStatus.Finished;
                    await db.SaveChangesAsync(ct);
                }
                return;
            }

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

    private async Task CompleteScoringAsync(
        IServiceProvider scopedServices,
        ApplicationDbContext db,
        Competition competition,
        AwdpRound round,
        int totalRounds,
        DateTime now,
        CancellationToken ct,
        bool startNextRound = true)
    {
        await scopedServices
            .GetRequiredService<AwdpScoreEngine>()
            .CalculateRoundScoreAsync(competition.Id, round.RoundNumber, ct);

        round.Status = AwdpRoundStatus.RoundFinished;
        round.EndTime ??= now;
        if (!startNextRound || round.RoundNumber >= totalRounds)
            competition.Status = CompetitionStatus.Finished;
        await db.SaveChangesAsync(ct);

        if (startNextRound && round.RoundNumber < totalRounds)
        {
            await StartRoundAsync(
                scopedServices,
                db,
                competition,
                round.RoundNumber + 1,
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
            // The durable round transition is authoritative; a transient
            // realtime failure must not stop the round engine from recovering.
            logger.LogWarning(
                ex,
                "Competition {CompetitionId}: AWDP round {RoundNumber} notification failed.",
                competition.Id,
                roundNumber);
        }
    }
}
