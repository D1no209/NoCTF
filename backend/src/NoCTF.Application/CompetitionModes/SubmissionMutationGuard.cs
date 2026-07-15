using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using NoCTF.Application.BackgroundTasks;
using NoCTF.Core;
using NoCTF.Infrastructure;
using NoCTF.PluginBase;

namespace NoCTF.Application.CompetitionModes;

public sealed record SubmissionMutationGuardResult(
    SubmissionResult? Rejection,
    Challenge? Challenge)
{
    public bool IsAllowed => Rejection is null;
}

/// <summary>
/// Fences submission writes against destructive lifecycle transitions. The
/// caller must hold <see cref="CompetitionExecutionLeaseKeys.RuntimePreparation"/>
/// until its database transaction commits.
/// </summary>
public static class SubmissionMutationGuard
{
    private static readonly TimeSpan DefaultLeaseWait = TimeSpan.FromSeconds(2);

    public static async Task<IExecutionLease?> TryAcquireAsync(
        ICompetitionExecutionLease executionLease,
        ApplicationDbContext db,
        Guid competitionId,
        CancellationToken ct,
        TimeSpan? maxWait = null)
    {
        var wait = maxWait ?? DefaultLeaseWait;
        var startedAt = Stopwatch.GetTimestamp();
        var retryDelay = TimeSpan.FromMilliseconds(10);

        while (true)
        {
            var lease = await executionLease.TryAcquireAsync(
                db,
                CompetitionExecutionLeaseKeys.RuntimePreparation,
                competitionId,
                ct);
            if (lease is not null)
                return lease;

            if (Stopwatch.GetElapsedTime(startedAt) >= wait)
                return null;

            await Task.Delay(retryDelay, ct);
            if (retryDelay < TimeSpan.FromMilliseconds(50))
                retryDelay += TimeSpan.FromMilliseconds(10);
        }
    }

    public static async Task<SubmissionMutationGuardResult> ValidateAsync(
        ApplicationDbContext db,
        Guid competitionId,
        Guid challengeId,
        IReadOnlyCollection<Guid> requiredTeamIds,
        DateTime now,
        CancellationToken ct,
        GameModeType? expectedGameMode = null,
        string? expectedChallengeType = null)
    {
        var target = await (
                from competition in db.Competitions.IgnoreQueryFilters().AsNoTracking()
                join challenge in db.Challenges.IgnoreQueryFilters().AsNoTracking()
                    on competition.Id equals challenge.CompetitionId
                where competition.Id == competitionId && challenge.Id == challengeId
                select new
                {
                    competition.GameModeType,
                    competition.StartTime,
                    competition.EndTime,
                    competition.Status,
                    Challenge = challenge
                })
            .SingleOrDefaultAsync(ct);

        if (target is null || target.Challenge.IsDeleting)
            return new SubmissionMutationGuardResult(SubmissionResult.WrongFlag, null);

        if (expectedGameMode.HasValue && target.GameModeType != expectedGameMode.Value)
            return new SubmissionMutationGuardResult(SubmissionResult.NotImplemented, null);

        if (!string.IsNullOrWhiteSpace(expectedChallengeType) &&
            !string.Equals(target.Challenge.TypeId, expectedChallengeType, StringComparison.OrdinalIgnoreCase))
        {
            return new SubmissionMutationGuardResult(SubmissionResult.WrongFlag, null);
        }

        if (now < target.StartTime || target.Status == CompetitionStatus.Draft)
            return new SubmissionMutationGuardResult(SubmissionResult.CompetitionNotStarted, null);

        if (now > target.EndTime || target.Status == CompetitionStatus.Finished)
            return new SubmissionMutationGuardResult(SubmissionResult.CompetitionEnded, null);

        if (target.Status == CompetitionStatus.Paused)
            return new SubmissionMutationGuardResult(SubmissionResult.CompetitionPaused, null);

        var teamIds = requiredTeamIds.Distinct().ToArray();
        if (teamIds.Length > 0)
        {
            var activeTeamCount = await db.Teams
                .IgnoreQueryFilters()
                .AsNoTracking()
                .CountAsync(team =>
                    team.CompetitionId == competitionId &&
                    teamIds.Contains(team.Id) &&
                    team.RegistrationStatus == TeamRegistrationStatus.Approved &&
                    !team.IsBanned,
                    ct);
            if (activeTeamCount != teamIds.Length)
                return new SubmissionMutationGuardResult(SubmissionResult.WrongFlag, null);
        }

        return new SubmissionMutationGuardResult(null, target.Challenge);
    }
}
