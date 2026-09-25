using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using NoCTF.Application.GameplayFacts.Intake;
using NoCTF.Application.Observability;
using NoCTF.Domain.Challenges;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Gameplay;
using NoCTF.Infrastructure.Competitions.Lifecycle;
using NoCTF.Infrastructure.Persistence;

namespace NoCTF.Infrastructure.GameplayFacts.Intake;

public sealed class FlagAttemptStateReader(NoCtfDbContext db) : IFlagAttemptStateReader
{
    public async Task<FlagAttemptState?> ReadAsync(
        Guid competitionId,
        Guid competitionChallengeId,
        Guid teamId,
        GameMode mode,
        CompetitionStatus status,
        CancellationToken cancellationToken)
    {
        var started = Stopwatch.GetTimestamp();
        using var activity = NoCtfTelemetry.ActivitySource.StartActivity("Challenge.AttemptStateRead");
        try
        {
            var rules = await db.Set<CompetitionChallengeRules>().AsNoTracking()
                .Where(item => item.CompetitionChallengeId == competitionChallengeId)
                .Select(item => new { item.MaxFlagAttempts, item.MaxBreakSubmissions })
                .SingleOrDefaultAsync(cancellationToken);
            if (rules is null)
                throw new InvalidOperationException("Challenge rules are required for attempt state.");

            var practice = mode == GameMode.Ctf && status == CompetitionStatus.Finished;
            int? maximum = mode switch
            {
                GameMode.Ctf => rules.MaxFlagAttempts,
                GameMode.Awdp => rules.MaxBreakSubmissions ?? await db.Set<AwdpCompetitionModeConfiguration>()
                    .AsNoTracking()
                    .Where(item => item.CompetitionId == competitionId)
                    .Select(item => (int?)item.MaxBreakSubmissions)
                    .SingleAsync(cancellationToken),
                _ => null
            };
            if (maximum is <= 0)
                maximum = null;

            var attempts = db.GameplayFacts.AsNoTracking()
                .Where(item => item.CompetitionId == competitionId
                    && item.CompetitionChallengeId == competitionChallengeId
                    && item.TeamId == teamId
                    && item.State != GameplayFactState.PlatformFailed);
            if (mode == GameMode.Ctf)
            {
                var window = await db.Competitions.AsNoTracking()
                    .Where(item => item.Id == competitionId)
                    .Select(item => new { item.StartAt, item.EndAt, item.PracticeModeEnabled })
                    .SingleAsync(cancellationToken);
                if (practice && window.PracticeModeEnabled)
                    maximum = null;
                var officialWindow = await CompetitionOfficialWindowReader.ReadAsync(
                    db, competitionId, window.StartAt, window.EndAt, cancellationToken);
                attempts = practice
                    ? attempts.Where(item => item.OccurredAt >= officialWindow.EndAt)
                    : attempts.Where(item => item.OccurredAt >= officialWindow.StartAt
                        && item.OccurredAt < officialWindow.EndAt);
            }

            var totals = await attempts
                .Where(item => item.Kind == GameplayFactKind.FlagAttempt
                    || item.Kind == GameplayFactKind.BreakAttempt)
                .GroupBy(item => item.Kind)
                .Select(group => new
                {
                    Kind = group.Key,
                    Count = group.Count(),
                    HasCorrect = group.Any(item => item.Result == GameplayFactResult.Correct)
                })
                .ToArrayAsync(cancellationToken);
            var accepted = totals.Sum(item => item.Count);
            var solved = mode == GameMode.Ctf
                && totals.Any(item => item.Kind == GameplayFactKind.FlagAttempt && item.HasCorrect);
            return new(maximum, accepted,
                maximum is { } limit ? Math.Max(0, limit - accepted) : null,
                solved);
        }
        finally
        {
            NoCtfTelemetry.RecordGameplayFactStage(
                GameplayFactPerformanceStage.ChallengeAttemptStateRead,
                Stopwatch.GetElapsedTime(started).TotalSeconds);
        }
    }
}
