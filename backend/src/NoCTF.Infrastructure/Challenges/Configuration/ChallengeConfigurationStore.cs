using NoCTF.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Challenges.Configuration;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Teams;
using NoCTF.Application.Messaging;
using NoCTF.Application.Competitions.Events;
using NoCTF.Domain.Competitions.Events;
using NoCTF.Domain.Challenges;

namespace NoCTF.Infrastructure.Challenges.Configuration;

public sealed class ChallengeConfigurationStore(
    NoCtfDbContext db,
    IPostCommitMessagePublisher outbox,
    ICompetitionEventRecorder events) : IChallengeConfigurationStore
{
    public async Task<ChallengeConfigurationView?> FindAsync(
        Guid competitionId,
        Guid challengeId,
        CancellationToken ct)
    {
        var item = await db.CompetitionChallenges.AsNoTracking()
            .Join(db.Challenges.AsNoTracking(), configuration => configuration.ChallengeId, challenge => challenge.Id,
                (configuration, challenge) => new { Configuration = configuration, Challenge = challenge })
            .Join(db.Competitions.AsNoTracking(), item => item.Configuration.CompetitionId, competition => competition.Id,
                (item, competition) => new { item.Configuration, item.Challenge, Competition = competition })
            .Where(item => item.Configuration.Id == challengeId
                           && item.Configuration.CompetitionId == competitionId
                           && item.Challenge.DeletedAt == null
                           && item.Competition.DeletedAt == null)
            .AsSplitQuery()
            .SingleOrDefaultAsync(ct);
        if (item?.Configuration.Rules is null
            || item.Challenge.Definition is null
            || item.Competition.ModeConfiguration is null)
            return null;
        var eligibleTeamCount = await db.Teams.CountAsync(team =>
            team.CompetitionId == item.Competition.Id
            && team.RegistrationStatus == TeamRegistrationStatus.Approved
            && !team.IsBanned
            && team.DeletedAt == null, ct);
        return new(
            item.Competition.Id,
            item.Configuration.Id,
            item.Competition.Mode,
            item.Configuration.Rules,
            item.Competition.ModeConfiguration,
            item.Competition.Status,
            eligibleTeamCount,
            item.Configuration.UpdatedAt,
            item.Challenge.Definition);
    }

    public async Task<ChallengeConfigurationUpdateResult> TryUpdateAsync(
        Guid competitionId,
        Guid challengeId,
        CompetitionChallengeRules rules,
        DateTimeOffset updatedAt,
        CancellationToken ct)
    {
        await using var transaction = await AggregateCompatibleTransaction.BeginAsync(db, ct);
        var status = await CompetitionStateReader.ReadAsync(db, competitionId, ct);
        if (status is null) return new(null, ChallengeConfigurationUpdateFailure.CompetitionNotFound);
        var competition = await db.Competitions.AsNoTracking()
            .AsSplitQuery()
            .SingleOrDefaultAsync(candidate => candidate.Id == competitionId, ct);
        if (competition is null)
            return new(null, ChallengeConfigurationUpdateFailure.CompetitionNotFound);
        var tracked = await db.CompetitionChallenges
            .AsSplitQuery()
            .SingleOrDefaultAsync(challenge => challenge.Id == challengeId
                && challenge.CompetitionId == competitionId
                && challenge.DeletedAt == null, ct);
        if (tracked?.Rules is null)
            return new(null, ChallengeConfigurationUpdateFailure.ChallengeNotFound);
        if (tracked.Mode != rules.Mode || tracked.Rules.GetType() != rules.GetType())
            throw new InvalidOperationException("Challenge rules type does not match its mode.");
        rules.CompetitionChallengeId = challengeId;
        var bloodChanged = !tracked.Rules.BloodRewards.OrderBy(item => item.Position).Select(item => (item.Policy, item.Value))
            .SequenceEqual(rules.BloodRewards.OrderBy(item => item.Position).Select(item => (item.Policy, item.Value)));
        db.Entry(tracked.Rules).CurrentValues.SetValues(rules);
        db.ChangeTracker.DetectChanges();
        if (db.Entry(tracked.Rules).State == EntityState.Unchanged && !bloodChanged)
        {
            var unchanged = await FindAsync(competitionId, challengeId, ct);
            await transaction.CommitAsync(ct);
            return new(unchanged);
        }
        if (bloodChanged)
        {
            db.Set<CompetitionChallengeBloodReward>().RemoveRange(tracked.Rules.BloodRewards);
            tracked.Rules.BloodRewards = rules.BloodRewards.Select((reward, position) =>
                new CompetitionChallengeBloodReward
                {
                    CompetitionChallengeId = challengeId,
                    Position = position,
                    Policy = reward.Policy,
                    Value = reward.Value
                }).ToList();
        }
        tracked.UpdatedAt = updatedAt;
        if (competition.Mode == GameMode.Awd
            && status == CompetitionStatus.Running
            && tracked.IsPublished)
        {
            await outbox.PublishAsync(new AdvanceAwdRound(
                competitionId,
                challengeId,
                updatedAt));
        }
        await events.RecordAsync(new(
            competitionId,
            CompetitionEventKind.ChallengeUpdated,
            CompetitionEventLevel.Information,
            CompetitionEventVisibility.Staff,
            updatedAt,
            CompetitionChallengeId: challengeId,
            CompetitionStatus: status), ct);
        await db.SaveChangesAsync(ct);
        var result = await FindAsync(competitionId, challengeId, ct);
        await transaction.CommitAsync(ct);
        await transaction.FlushMessagesAsync(outbox);
        return new(result);
    }
}
