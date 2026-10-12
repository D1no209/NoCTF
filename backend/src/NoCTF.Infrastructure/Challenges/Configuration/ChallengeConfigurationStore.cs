using NoCTF.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Challenges.Configuration;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Teams;
using NoCTF.Application.Messaging;
using NoCTF.Application.Competitions.Events;
using NoCTF.Domain.Competitions.Events;
using NoCTF.Domain.Challenges;
using NoCTF.Domain.Competitions.Awd;

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
        var storesChanged = tracked.Rules is AwdCompetitionChallengeRules beforeAwd
            && rules is AwdCompetitionChallengeRules afterAwd
            && !beforeAwd.StoreRules.OrderBy(item => item.StoreId).Select(item => (item.StoreId, item.Weight, item.FlagsPerIssue))
                .SequenceEqual(afterAwd.StoreRules.OrderBy(item => item.StoreId).Select(item => (item.StoreId, item.Weight, item.FlagsPerIssue)));
        if (rules is AwdCompetitionChallengeRules requestedAwd)
        {
            var definition = await db.Set<AwdChallengeDefinition>().AsNoTracking().AsSplitQuery()
                .SingleAsync(item => item.ChallengeId == tracked.ChallengeId, ct);
            var storeIds = definition.FlagStores.Select(item => item.Id).ToArray();
            if (requestedAwd.StoreRules.Any(item => !storeIds.Contains(item.StoreId)))
                return new(null, ChallengeConfigurationUpdateFailure.ConfigurationLocked);
        }
        db.Entry(tracked.Rules).CurrentValues.SetValues(rules);
        if (tracked.Rules is AwdCompetitionChallengeRules currentAwd && rules is AwdCompetitionChallengeRules nextAwd)
            currentAwd.AwdOverrides = nextAwd.AwdOverrides with { };
        db.ChangeTracker.DetectChanges();
        if (db.Entry(tracked.Rules).State == EntityState.Unchanged && !bloodChanged && !storesChanged)
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
        if (storesChanged && tracked.Rules is AwdCompetitionChallengeRules storedAwd
            && rules is AwdCompetitionChallengeRules requestedStores)
        {
            var requestedIds = requestedStores.StoreRules.Select(item => item.StoreId).ToHashSet();
            foreach (var old in storedAwd.StoreRules.Where(item => !requestedIds.Contains(item.StoreId)).ToArray())
            {
                storedAwd.StoreRules.Remove(old);
                db.Remove(old);
            }
            foreach (var item in requestedStores.StoreRules)
            {
                var existing = storedAwd.StoreRules.SingleOrDefault(old => old.StoreId == item.StoreId);
                if (existing is null)
                    storedAwd.StoreRules.Add(new() { CompetitionChallengeId = challengeId,
                        StoreId = item.StoreId, Weight = item.Weight, FlagsPerIssue = item.FlagsPerIssue });
                else
                {
                    existing.Weight = item.Weight;
                    existing.FlagsPerIssue = item.FlagsPerIssue;
                }
            }
        }
        tracked.UpdatedAt = updatedAt;
        if (competition.Mode == GameMode.Awd
            && status == CompetitionStatus.Running
            && tracked.IsPublished)
        {
            await outbox.PublishAsync(new AdvanceAwdRound(
                competitionId,
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
