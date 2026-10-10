using NoCTF.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Competitions.Configuration;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Teams;
using NoCTF.Application.Messaging;
using NoCTF.Application.Competitions.Events;
using NoCTF.Domain.Competitions.Events;
using NoCTF.Domain.LiveSolo;

namespace NoCTF.Infrastructure.Competitions.Configuration;

public sealed class CompetitionConfigurationStore(
    NoCtfDbContext db,
    IPostCommitMessagePublisher outbox,
    ICompetitionEventRecorder events) : ICompetitionConfigurationStore
{
    public async Task<CompetitionConfigurationView?> FindAsync(
        Guid competitionId,
        CancellationToken ct)
    {
        var competition = await db.Competitions.AsNoTracking().SingleOrDefaultAsync(
            item => item.Id == competitionId && item.DeletedAt == null, ct);
        if (competition?.ModeConfiguration is null)
            return null;
        var eligibleTeamCount = await db.Teams.CountAsync(team =>
            team.CompetitionId == competition.Id
            && team.RegistrationStatus == TeamRegistrationStatus.Approved
            && !team.IsBanned
            && team.DeletedAt == null, ct);
        var challengeEntities = await db.CompetitionChallenges.AsNoTracking()
            .Where(challenge => challenge.CompetitionId == competition.Id
                && challenge.DeletedAt == null)
            .OrderBy(challenge => challenge.Id)
            .ToArrayAsync(ct);
        return new(
            competition.Id,
            competition.Mode,
            competition.ModeConfiguration,
            competition.Status,
            eligibleTeamCount,
            challengeEntities.Select(challenge =>
                new CompetitionChallengeConfigurationSnapshot(
                    challenge.Id,
                    challenge.Rules ?? throw new InvalidOperationException(
                        $"Competition challenge {challenge.Id} has no rules."))).ToArray(),
            competition.UpdatedAt);
    }

    public async Task<CompetitionConfigurationUpdateResult> TryUpdateAsync(
        Guid competitionId,
        CompetitionModeConfiguration configuration,
        bool allowWhileRunning,
        DateTimeOffset now,
        CancellationToken ct)
    {
        await using var transaction = await AggregateCompatibleTransaction.BeginAsync(db, ct);
        var status = await CompetitionStateReader.ReadAsync(db, competitionId, ct);
        if (status is null) return new(null, CompetitionConfigurationUpdateFailure.CompetitionNotFound);
        var tracked = await db.Competitions.SingleOrDefaultAsync(
            competition => competition.Id == competitionId, ct);
        if (tracked?.ModeConfiguration is null)
            return new(null, CompetitionConfigurationUpdateFailure.CompetitionNotFound);
        if (tracked.Mode != configuration.Mode
            || tracked.ModeConfiguration.GetType() != configuration.GetType())
            throw new InvalidOperationException("Competition configuration type does not match its mode.");
        configuration.CompetitionId = competitionId;
        if (tracked.ModeConfiguration is LiveSoloCompetitionModeConfiguration existingLive
            && configuration is LiveSoloCompetitionModeConfiguration nextLive && existingLive.BracketFormat != nextLive.BracketFormat
            && await db.LiveSoloMatches.AnyAsync(x => x.CompetitionId == competitionId, ct))
            return new(null, CompetitionConfigurationUpdateFailure.ConfigurationLocked);
        var stagesChanged = tracked.ModeConfiguration is LiveSoloCompetitionModeConfiguration beforeLive
            && configuration is LiveSoloCompetitionModeConfiguration afterLive
            && !beforeLive.StageRules.OrderBy(x => x.Lane).ThenBy(x => x.Stage).Select(x => (x.Lane, x.Stage, x.RequiredWins))
                .SequenceEqual(afterLive.StageRules.OrderBy(x => x.Lane).ThenBy(x => x.Stage).Select(x => (x.Lane, x.Stage, x.RequiredWins)));
        var bloodChanged = tracked.ModeConfiguration is CtfCompetitionModeConfiguration beforeCtf
            && configuration is CtfCompetitionModeConfiguration afterCtf
            && !beforeCtf.BloodRewards.OrderBy(item => item.Position).Select(item => (item.Policy, item.Value))
                .SequenceEqual(afterCtf.BloodRewards.OrderBy(item => item.Position).Select(item => (item.Policy, item.Value)));
        db.Entry(tracked.ModeConfiguration).CurrentValues.SetValues(configuration);
        db.ChangeTracker.DetectChanges();
        if (db.Entry(tracked.ModeConfiguration).State == EntityState.Unchanged && !bloodChanged && !stagesChanged)
        {
            var unchanged = await FindAsync(competitionId, ct);
            await transaction.CommitAsync(ct);
            return new(unchanged);
        }
        if (tracked.ModeConfiguration is CtfCompetitionModeConfiguration currentCtf
            && configuration is CtfCompetitionModeConfiguration nextCtf && bloodChanged)
        {
            db.Set<CompetitionBloodReward>().RemoveRange(currentCtf.BloodRewards);
            currentCtf.BloodRewards = nextCtf.BloodRewards.Select((reward, position) =>
                new CompetitionBloodReward
                {
                    CompetitionId = competitionId,
                    Position = position,
                    Policy = reward.Policy,
                    Value = reward.Value
                }).ToList();
        }
        if (tracked.ModeConfiguration is LiveSoloCompetitionModeConfiguration currentLive
            && configuration is LiveSoloCompetitionModeConfiguration requestedLive && stagesChanged)
        {
            db.Set<LiveSoloStageRule>().RemoveRange(currentLive.StageRules);
            currentLive.StageRules.Clear();
            await db.SaveChangesAsync(ct);
            currentLive.StageRules = requestedLive.StageRules.Select(x => new LiveSoloStageRule {
                CompetitionId = competitionId, Lane = x.Lane, Stage = x.Stage, RequiredWins = x.RequiredWins }).ToList();
        }
        tracked.UpdatedAt = now;
        if (tracked.Mode == GameMode.Awd && status == CompetitionStatus.Running)
        {
            var challenges = await db.CompetitionChallenges.AsNoTracking()
                .Where(challenge => challenge.CompetitionId == competitionId
                    && challenge.IsPublished
                    && challenge.DeletedAt == null)
                .Select(challenge => challenge.Id)
                .ToListAsync(ct);
            foreach (var challengeId in challenges)
            {
                await outbox.PublishAsync(new AdvanceAwdRound(
                    competitionId,
                    challengeId,
                    now));
            }
        }
        await events.RecordAsync(new(
            competitionId,
            CompetitionEventKind.CompetitionUpdated,
            CompetitionEventLevel.Information,
            CompetitionEventVisibility.Staff,
            now,
            CompetitionStatus: status), ct);
        await db.SaveChangesAsync(ct);
        var result = await FindAsync(competitionId, ct);
        await transaction.CommitAsync(ct);
        await transaction.FlushMessagesAsync(outbox);
        return new(result);
    }
}
