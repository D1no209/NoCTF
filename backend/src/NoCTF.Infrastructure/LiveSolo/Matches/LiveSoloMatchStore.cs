using System.Data;
using Microsoft.EntityFrameworkCore;
using NoCTF.Application.LiveSolo.Matches;
using NoCTF.Application.LiveSolo.Media;
using NoCTF.Application.LiveSolo.Resources;
using NoCTF.Application.LiveSolo.Rounds;
using NoCTF.Application.Messaging;
using NoCTF.Application.Commands.Idempotency;
using NoCTF.Application.Teams.Moderation;
using NoCTF.Domain.Identity;
using NoCTF.Domain.LiveSolo;
using NoCTF.Domain.Teams;
using NoCTF.GameModes.LiveSolo.Configuration;
using NoCTF.Infrastructure.Persistence;

namespace NoCTF.Infrastructure.LiveSolo.Matches;

public sealed partial class LiveSoloMatchStore(NoCtfDbContext db, ICompetitionModerationAuthorizer authorizer,
    ILiveSoloMediaGateway media, ILiveSoloRuntimePreparation runtimePreparation, IPostCommitMessagePublisher messages,
    IRequestReplay? replay = null, TimeProvider? clock = null)
    : ILiveSoloMatchStore
{
    private async Task<T> TransactionAsync<T>(Func<Task<T>> work, Func<T> conflict, CancellationToken ct)
    {
        for (var attempt = 0; ; attempt++)
        {
            var committed = false;
            try
            {
                await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
                var result = await work();
                await db.SaveChangesAsync(ct); await tx.CommitAsync(ct); committed = true;
                await messages.FlushCommittedMessagesAsync(); return result;
            }
            catch (Exception ex) when (!committed && attempt < 2 && (ex is DbUpdateException || TransactionFailureClassifier.IsRetryable(ex)))
            { db.ChangeTracker.Clear(); messages.DiscardPendingMessages(); }
            catch (Exception ex) when (!committed && (ex is DbUpdateException || TransactionFailureClassifier.IsRetryable(ex)))
            { db.ChangeTracker.Clear(); messages.DiscardPendingMessages(); return conflict(); }
        }
    }

    private async Task<bool> ActiveAsync(Guid actor, CancellationToken ct) => actor != Guid.Empty
        && await db.Users.AnyAsync(x => x.Id == actor && x.AccountStatus == UserAccountStatus.Active, ct);
    private async Task<Guid?> TeamAsync(Guid competition, Guid actor, CancellationToken ct) => await db.Teams.AsNoTracking()
        .Where(x => x.CompetitionId == competition && !x.IsBanned && x.RegistrationStatus == TeamRegistrationStatus.Approved
            && x.Members.Any(m => m.UserId == actor)).Select(x => (Guid?)x.Id).SingleOrDefaultAsync(ct);
    private async Task<LiveSoloMatch?> MatchAsync(Guid competition, Guid match, CancellationToken ct) => await db.LiveSoloMatches
        .Include(x => x.Slots).Include(x => x.Roster).SingleOrDefaultAsync(x => x.Id == match && x.CompetitionId == competition, ct);
    private async Task<LiveSoloMatchView> MapAsync(LiveSoloMatch match, CancellationToken ct)
    {
        var ids = match.Slots.Where(x => x.TeamId is not null).Select(x => x.TeamId!.Value).ToArray();
        var names = await db.Teams.IgnoreQueryFilters().Where(x => ids.Contains(x.Id)).ToDictionaryAsync(x => x.Id, x => x.Name, ct);
        return Map(match, names);
    }
    private static LiveSoloMatchView Map(LiveSoloMatch match, IReadOnlyDictionary<Guid, string> names)
    {
        var left = match.Slots.Single(x => x.Side == LiveSoloSide.Left); var right = match.Slots.Single(x => x.Side == LiveSoloSide.Right);
        return new(match.Id, match.CompetitionId, match.State, match.ConcurrencyStamp, match.RequiredWins, match.LeftWins, match.RightWins,
            left.TeamId, left.TeamId is Guid l ? names.GetValueOrDefault(l) : null,
            right.TeamId, right.TeamId is Guid r ? names.GetValueOrDefault(r) : null, match.CurrentRoundId, match.WinnerTeamId,
            match.Slots.Where(x => x.TeamId is not null).Select(x => new LiveSoloRosterView(x.TeamId!.Value,
                match.Roster.Where(r => r.TeamId == x.TeamId).Select(r => r.UserId).Order().ToArray(), x.RosterLockedAt is not null, x.ReadyConfirmedAt is not null)).ToArray());
    }

    public async Task<LiveSoloMatchResult> CreateAsync(CreateLiveSoloMatch command, CancellationToken ct) =>
        await TransactionAsync(async () =>
        {
            if (!await ActiveAsync(command.ActorId, ct) || !await authorizer.CanModerateAsync(command.ActorId, command.CompetitionId, ct)) return new LiveSoloMatchResult(null, LiveSoloFailure.Forbidden);
            var configuration = await db.Set<LiveSoloCompetitionModeConfiguration>().AsNoTracking().SingleOrDefaultAsync(x => x.CompetitionId == command.CompetitionId, ct);
            if (configuration is null) return new(null, LiveSoloFailure.NotFound);
            var ids = new[] { command.LeftTeamId, command.RightTeamId };
            if (ids.Distinct().Count() != 2 || await db.Teams.CountAsync(x => ids.Contains(x.Id) && x.CompetitionId == command.CompetitionId
                && !x.IsBanned && x.RegistrationStatus == TeamRegistrationStatus.Approved, ct) != 2) return new(null, LiveSoloFailure.InvalidConfiguration);
            if (await db.LiveSoloActiveTeamSlots.AnyAsync(x => x.CompetitionId == command.CompetitionId && ids.Contains(x.TeamId), ct)) return new(null, LiveSoloFailure.Conflict);
            var position = (await db.LiveSoloMatches.Where(x => x.CompetitionId == command.CompetitionId && x.Lane == LiveSoloBracketLane.Winners && x.Stage == 1)
                .Select(x => (int?)x.Position).MaxAsync(ct) ?? -1) + 1;
            var match = new LiveSoloMatch { Id = Guid.CreateVersion7(command.Now), CompetitionId = command.CompetitionId, Lane = LiveSoloBracketLane.Winners,
                Stage = 1, Position = position, RequiredWins = command.RequiredWins ?? configuration.RequiredWins,
                State = LiveSoloMatchState.Preparing, CreatedAt = command.Now, Slots = [
                    new() { Side = LiveSoloSide.Left, Source = LiveSoloSlotSource.Seed, TeamId = command.LeftTeamId, Resolved = true },
                    new() { Side = LiveSoloSide.Right, Source = LiveSoloSlotSource.Seed, TeamId = command.RightTeamId, Resolved = true }] };
            db.LiveSoloMatches.Add(match);
            db.LiveSoloActiveTeamSlots.AddRange(ids.Select(id => new LiveSoloActiveTeamSlot { CompetitionId = command.CompetitionId, TeamId = id, MatchId = match.Id }));
            await db.SaveChangesAsync(ct);
            return new(await MapAsync(match, ct));
        }, () => new(null, LiveSoloFailure.Conflict), ct);

    public async Task<LiveSoloMatchView?> FindAsync(Guid competitionId, Guid matchId, Guid actorId, bool staff, DateTimeOffset now, CancellationToken ct)
    {
        if (!await ActiveAsync(actorId, ct) || !await db.Competitions.AnyAsync(x => x.Id == competitionId, ct)) return null;
        var team = await TeamAsync(competitionId, actorId, ct);
        if (staff && !await authorizer.CanObserveAsync(actorId, competitionId, ct)) return null;
        var match = await MatchAsync(competitionId, matchId, ct);
        return match is null || !staff && !match.Slots.Any(x => x.TeamId == team && team is not null) ? null : await MapAsync(match, ct);
    }
    public async Task<IReadOnlyList<LiveSoloMatchView>?> ListAsync(Guid competitionId, Guid actorId, bool staff, DateTimeOffset now, CancellationToken ct)
    {
        if (!await ActiveAsync(actorId, ct) || !await db.Competitions.AnyAsync(x => x.Id == competitionId, ct)) return null;
        if (staff && !await authorizer.CanObserveAsync(actorId, competitionId, ct)) return null;
        var team = await TeamAsync(competitionId, actorId, ct);
        if (!staff && team is null) return null;
        var matches = await db.LiveSoloMatches.AsNoTracking().Include(x => x.Slots).Include(x => x.Roster)
            .Where(x => x.CompetitionId == competitionId && (staff || x.Slots.Any(s => s.TeamId == team)))
            .OrderBy(x => x.Lane).ThenBy(x => x.Stage).ThenBy(x => x.Position).ToArrayAsync(ct);
        var teamIds = matches.SelectMany(x => x.Slots).Where(x => x.TeamId != null).Select(x => x.TeamId!.Value).Distinct().ToArray();
        var names = await db.Teams.IgnoreQueryFilters().Where(x => teamIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, x => x.Name, ct);
        return matches.Select(x => Map(x, names)).ToArray();
    }

    public async Task<LiveSoloQuestionGroupResult> SaveGroupAsync(Guid competitionId, Guid actorId, LiveSoloQuestionGroupInput input, DateTimeOffset now, CancellationToken ct) =>
        await TransactionAsync(async () =>
        {
            if (!await ActiveAsync(actorId, ct) || !await authorizer.CanModerateAsync(actorId, competitionId, ct)) return new LiveSoloQuestionGroupResult(null, LiveSoloFailure.Forbidden);
            var config = await db.Set<LiveSoloCompetitionModeConfiguration>().SingleOrDefaultAsync(x => x.CompetitionId == competitionId, ct);
            if (config is null) return new(null, LiveSoloFailure.NotFound);
            var group = input.Id is Guid id ? await db.LiveSoloQuestionGroups.Include(x => x.Items).SingleOrDefaultAsync(x => x.Id == id && x.CompetitionId == competitionId, ct)
                : new LiveSoloQuestionGroup { Id = Guid.CreateVersion7(now), CompetitionId = competitionId,
                    Position = (await db.LiveSoloQuestionGroups.Where(x => x.CompetitionId == competitionId).Select(x => (int?)x.Position).MaxAsync(ct) ?? -1) + 1 };
            if (group is null) return new(null, LiveSoloFailure.NotFound);
            if (input.Id is not null && group.ConcurrencyStamp != input.ExpectedStamp) return new(null, LiveSoloFailure.Conflict);
            if (input.Id is not null && await db.LiveSoloRounds.AnyAsync(x => x.QuestionGroupId == group.Id
                && (x.State == LiveSoloRoundState.Preparing || x.State == LiveSoloRoundState.Countdown || x.State == LiveSoloRoundState.Running || x.State == LiveSoloRoundState.ConfirmingResult), ct)) return new(null, LiveSoloFailure.Conflict);
            var entries = input.Questions.Select((x, i) => new LiveSoloQuestionGroupItem { QuestionGroupId = group.Id, Position = i,
                CompetitionChallengeId = x.CompetitionChallengeId, OpenOffsetSeconds = x.OpenOffsetSeconds }).ToList();
            var candidate = new LiveSoloQuestionGroup { Name = input.Name.Trim(), RoundLimitSeconds = input.LimitSeconds, Items = entries };
            if (LiveSoloConfigurationValidator.ValidateGroup(candidate, config.RoundLimitSeconds, config.QuestionIntervalSeconds).Count != 0
                || await db.CompetitionChallenges.CountAsync(x => x.CompetitionId == competitionId && entries.Select(e => e.CompetitionChallengeId).Contains(x.Id), ct) != entries.Count)
                return new(null, LiveSoloFailure.InvalidConfiguration);
            var templates = await db.CompetitionChallenges.AsNoTracking().Where(x => entries.Select(e => e.CompetitionChallengeId).Contains(x.Id))
                .Select(x => x.ChallengeId).ToArrayAsync(ct);
            var sources = await db.LiveSoloChallengeSources.AsNoTracking().Where(x => templates.Contains(x.ChallengeId))
                .ToDictionaryAsync(x => x.ChallengeId, x => x.CanonicalChallengeId, ct);
            if (templates.Select(id => sources.GetValueOrDefault(id, id)).Distinct().Count() != entries.Count)
                return new(null, LiveSoloFailure.InvalidConfiguration);
            group.Name = candidate.Name; group.RoundLimitSeconds = input.LimitSeconds; group.Reserve = input.Reserve;
            group.ConcurrencyStamp = Guid.NewGuid();
            if (input.Id is null) db.LiveSoloQuestionGroups.Add(group);
            else
            {
                db.RemoveRange(group.Items); group.Items.Clear();
                // Delete the old ordered children before reusing their keys inside the same transaction.
                await db.SaveChangesAsync(ct);
            }
            group.Items = entries; db.Set<LiveSoloQuestionGroupItem>().AddRange(entries);
            await db.SaveChangesAsync(ct); return new(Group(group));
        }, () => new(null, LiveSoloFailure.Conflict), ct);
    public async Task<IReadOnlyList<LiveSoloQuestionGroupView>?> GroupsAsync(Guid competitionId, Guid actorId, CancellationToken ct) =>
        !await ActiveAsync(actorId, ct) || !await authorizer.CanObserveAsync(actorId, competitionId, ct) ? null
            : (await db.LiveSoloQuestionGroups.AsNoTracking().Include(x => x.Items).Where(x => x.CompetitionId == competitionId).OrderBy(x => x.Position).ToArrayAsync(ct)).Select(Group).ToArray();
    private static LiveSoloQuestionGroupView Group(LiveSoloQuestionGroup group) => new(group.Id, group.Name, group.Reserve, group.RoundLimitSeconds,
        group.ConcurrencyStamp, group.Items.OrderBy(x => x.Position).Select(x => new LiveSoloQuestionGroupEntry(x.CompetitionChallengeId, x.OpenOffsetSeconds)).ToArray());
}
