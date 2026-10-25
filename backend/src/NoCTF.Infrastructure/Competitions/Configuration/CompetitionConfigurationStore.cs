using NoCTF.Application.Competitions.Awd;
using NoCTF.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Competitions.Configuration;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Competitions.Awd;
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
        var presets = await db.Set<NoCTF.Domain.Competitions.Scoring.CompetitionScorePreset>().AsNoTracking()
            .Where(item => item.CompetitionId == competitionId).ToArrayAsync(ct);
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
                        $"Competition challenge {challenge.Id} has no rules."),
                    NoCTF.Domain.Competitions.Scoring.CompetitionScorePreset.Match(challenge.Tags.Select(tag => tag.Name), presets))).ToArray(),
            competition.UpdatedAt);
    }

    public async Task<CompetitionConfigurationUpdateResult> TryUpdateAsync(
        Guid competitionId,
        CompetitionModeConfiguration configuration,
        bool allowWhileRunning,
        DateTimeOffset now,
        CancellationToken ct, AwdConfigurationChangeAudit? audit = null)
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
        var nextAwdRound = tracked.Mode == GameMode.Awd
            ? (await db.AwdRounds.Where(round => round.CompetitionId == competitionId).MaxAsync(round => (int?)round.Number, ct) ?? 0) + 1
            : (int?)null;
        if (nextAwdRound > 1 && audit?.HasValidReason != true)
            return new(null, CompetitionConfigurationUpdateFailure.RuleChangeReasonRequired);
        configuration.CompetitionId = competitionId;
        if (tracked.ModeConfiguration is AwdCompetitionModeConfiguration existingAwd
            && configuration is AwdCompetitionModeConfiguration requestedAwd
            && status is CompetitionStatus.Running or CompetitionStatus.Paused or CompetitionStatus.Finished)
        {
            var latestRound = await db.AwdRounds.Where(round => round.CompetitionId == competitionId)
                .MaxAsync(round => (int?)round.Number, ct) ?? 0;
            if (requestedAwd.HardeningDurationSeconds != existingAwd.HardeningDurationSeconds
                || requestedAwd.AwdOptions.TotalRounds is { } total && total < latestRound)
                return new(null, CompetitionConfigurationUpdateFailure.ConfigurationLocked);
        }
        if (tracked.ModeConfiguration is LiveSoloCompetitionModeConfiguration existingLive
            && configuration is LiveSoloCompetitionModeConfiguration nextLive
            && (existingLive.BracketFormat != nextLive.BracketFormat || !existingLive.TournamentStages.OrderBy(x => x.Position)
                .Select(x => (x.Position, x.Name, x.Format, x.RequiredWins, x.AdvanceCount, x.SwissAdvanceWins, x.SwissEliminateLosses, x.SwissQualificationRequiredWins, x.SwissEliminationRequiredWins))
                .SequenceEqual(nextLive.TournamentStages.OrderBy(x => x.Position)
                    .Select(x => (x.Position, x.Name, x.Format, x.RequiredWins, x.AdvanceCount, x.SwissAdvanceWins, x.SwissEliminateLosses, x.SwissQualificationRequiredWins, x.SwissEliminationRequiredWins))))
            && await db.LiveSoloMatches.AnyAsync(x => x.CompetitionId == competitionId, ct))
            return new(null, CompetitionConfigurationUpdateFailure.ConfigurationLocked);
        var stagesChanged = tracked.ModeConfiguration is LiveSoloCompetitionModeConfiguration beforeLive
            && configuration is LiveSoloCompetitionModeConfiguration afterLive
            && !beforeLive.StageRules.OrderBy(x => x.TournamentStage).ThenBy(x => x.Lane).ThenBy(x => x.Stage).Select(x => (x.TournamentStage, x.Lane, x.Stage, x.RequiredWins))
                .SequenceEqual(afterLive.StageRules.OrderBy(x => x.TournamentStage).ThenBy(x => x.Lane).ThenBy(x => x.Stage).Select(x => (x.TournamentStage, x.Lane, x.Stage, x.RequiredWins)));
        var tournamentChanged = tracked.ModeConfiguration is LiveSoloCompetitionModeConfiguration oldTournament
            && configuration is LiveSoloCompetitionModeConfiguration newTournament
            && !oldTournament.TournamentStages.OrderBy(x => x.Position).Select(x => (x.Name, x.Format, x.RequiredWins, x.AdvanceCount, x.SwissAdvanceWins, x.SwissEliminateLosses, x.SwissQualificationRequiredWins, x.SwissEliminationRequiredWins))
                .SequenceEqual(newTournament.TournamentStages.OrderBy(x => x.Position).Select(x => (x.Name, x.Format, x.RequiredWins, x.AdvanceCount, x.SwissAdvanceWins, x.SwissEliminateLosses, x.SwissQualificationRequiredWins, x.SwissEliminationRequiredWins)));
        var bloodChanged = tracked.ModeConfiguration is CtfCompetitionModeConfiguration beforeCtf
            && configuration is CtfCompetitionModeConfiguration afterCtf
            && !beforeCtf.BloodRewards.OrderBy(item => item.Position).Select(item => (item.Policy, item.Value))
                .SequenceEqual(afterCtf.BloodRewards.OrderBy(item => item.Position).Select(item => (item.Policy, item.Value)));
        var tieBreaksChanged = tracked.ModeConfiguration is AwdCompetitionModeConfiguration beforeAwd
            && configuration is AwdCompetitionModeConfiguration afterAwd
            && !beforeAwd.TieBreakers.OrderBy(item => item.Position).Select(item => (item.Field, item.Direction))
                .SequenceEqual(afterAwd.TieBreakers.OrderBy(item => item.Position).Select(item => (item.Field, item.Direction)));
        db.Entry(tracked.ModeConfiguration).CurrentValues.SetValues(configuration);
        if (tracked.ModeConfiguration is AwdCompetitionModeConfiguration currentAwd
            && configuration is AwdCompetitionModeConfiguration nextAwd)
            currentAwd.AwdOptions = nextAwd.AwdOptions with { };
        db.ChangeTracker.DetectChanges();
        if (db.Entry(tracked.ModeConfiguration).State == EntityState.Unchanged && !bloodChanged && !stagesChanged && !tieBreaksChanged && !tournamentChanged)
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
                CompetitionId = competitionId, TournamentStage = x.TournamentStage, Lane = x.Lane, Stage = x.Stage, RequiredWins = x.RequiredWins }).ToList();
            db.Set<LiveSoloStageRule>().AddRange(currentLive.StageRules);
        }
        if (tournamentChanged && tracked.ModeConfiguration is LiveSoloCompetitionModeConfiguration currentTournament
            && configuration is LiveSoloCompetitionModeConfiguration requestedTournament)
        {
            db.Set<LiveSoloTournamentStage>().RemoveRange(currentTournament.TournamentStages);
            currentTournament.TournamentStages.Clear();
            await db.SaveChangesAsync(ct);
            currentTournament.TournamentStages = requestedTournament.TournamentStages;
            db.Set<LiveSoloTournamentStage>().AddRange(currentTournament.TournamentStages);
        }
        if (tieBreaksChanged && tracked.ModeConfiguration is AwdCompetitionModeConfiguration ranking
            && configuration is AwdCompetitionModeConfiguration requestedRanking)
        {
            db.Set<AwdCompetitionTieBreak>().RemoveRange(ranking.TieBreakers);
            ranking.TieBreakers.Clear();
            // Release both position and field uniqueness before replacing the ordered list.
            // This remains inside the aggregate transaction with its audit and publication.
            await db.SaveChangesAsync(ct);
            ranking.TieBreakers = requestedRanking.TieBreakers.OrderBy(item => item.Position).Select((item, position) =>
                new AwdCompetitionTieBreak { CompetitionId = competitionId, Position = position,
                    Field = item.Field, Direction = item.Direction }).ToList();
        }
        tracked.UpdatedAt = now;
        if (tracked.Mode == GameMode.Awd && status == CompetitionStatus.Running)
        {
            await outbox.PublishAsync(new AdvanceAwdRound(competitionId, now));
        }
        await events.RecordAsync(new(
            competitionId,
            CompetitionEventKind.CompetitionUpdated,
            CompetitionEventLevel.Information,
            CompetitionEventVisibility.Staff,
            now,
            CompetitionStatus: status, ActorUserId: audit?.ActorUserId, Reason: audit?.Reason?.Trim(),
            AwdRuleTiming: nextAwdRound is null ? null : NoCTF.Domain.Competitions.Awd.AwdRuleChangeTiming.NextRound,
            AwdEffectiveFromRound: nextAwdRound), ct);
        await db.SaveChangesAsync(ct);
        var result = await FindAsync(competitionId, ct);
        await transaction.CommitAsync(ct);
        await transaction.FlushMessagesAsync(outbox);
        return new(result);
    }
}
