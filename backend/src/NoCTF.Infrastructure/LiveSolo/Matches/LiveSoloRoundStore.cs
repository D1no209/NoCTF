using Microsoft.EntityFrameworkCore;
using NoCTF.Application.LiveSolo.Matches;
using NoCTF.Application.LiveSolo.Rounds;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.LiveSolo;

namespace NoCTF.Infrastructure.LiveSolo.Matches;

public sealed partial class LiveSoloMatchStore
{
    public async Task<LiveSoloRoundResult> PrepareRoundAsync(PrepareLiveSoloRound command, CancellationToken ct)
    {
        var result = await TransactionAsync(async () =>
        {
            if (!await ActiveAsync(command.ActorId, ct) || !await authorizer.CanJudgeAsync(command.ActorId, command.CompetitionId, ct)) return new LiveSoloRoundResult(null, LiveSoloFailure.Forbidden);
            var match = await MatchAsync(command.CompetitionId, command.MatchId, ct);
            if (match is null) return new(null, LiveSoloFailure.NotFound);
            if (match.ConcurrencyStamp != command.ExpectedStamp) return new(null, LiveSoloFailure.Conflict);
            if (match.State != LiveSoloMatchState.Preparing || match.Slots.Any(x => x.TeamId is null || x.RosterLockedAt is null)) return new(null, LiveSoloFailure.NotReady);
            var config = await db.Set<LiveSoloCompetitionModeConfiguration>().AsNoTracking().SingleAsync(x => x.CompetitionId == command.CompetitionId, ct);
            if (!config.Enabled) return new(null, LiveSoloFailure.Disabled);
            var rounds = await db.LiveSoloRounds.Where(x => x.MatchId == match.Id).OrderBy(x => x.Number).ThenBy(x => x.Replay).ToArrayAsync(ct);
            if (rounds.Any(x => x.State is LiveSoloRoundState.Preparing or LiveSoloRoundState.Countdown or LiveSoloRoundState.Running or LiveSoloRoundState.ConfirmingResult))
                return new(null, LiveSoloFailure.AlreadyEnded);
            var group = await SuitableGroupAsync(match, command.GroupId, command.Now, ct);
            if (group is null) return new(null, LiveSoloFailure.NoSuitableQuestionGroup);
            var last = rounds.LastOrDefault();
            var replay = last?.State is LiveSoloRoundState.TimedOut or LiveSoloRoundState.Canceled;
            var round = new LiveSoloRound { Id = Guid.CreateVersion7(command.Now), MatchId = match.Id,
                Number = replay ? last!.Number : (last?.Number ?? 0) + 1, Replay = replay ? last!.Replay + 1 : 0,
                QuestionGroupId = group.Id, LimitSeconds = group.RoundLimitSeconds ?? config.RoundLimitSeconds,
                CountdownSeconds = config.CountdownSeconds, CreatedAt = command.Now, State = LiveSoloRoundState.Preparing,
                Questions = group.Items.OrderBy(x => x.Position).Select(x => new LiveSoloRoundQuestion { Id = Guid.CreateVersion7(),
                    CompetitionChallengeId = x.CompetitionChallengeId, Position = x.Position,
                    OpenOffsetSeconds = x.OpenOffsetSeconds ?? checked(x.Position * config.QuestionIntervalSeconds) }).ToList() };
            db.LiveSoloRounds.Add(round); match.CurrentRoundId = round.Id;
            foreach (var slot in match.Slots) slot.ReadyConfirmedAt = null;
            await db.SaveChangesAsync(ct); return new(Round(round, command.Now));
        }, () => new(null, LiveSoloFailure.Conflict), ct);
        if (result.Round is { } prepared)
            foreach (var question in await db.LiveSoloRoundQuestions.Where(x => x.RoundId == prepared.Id).Select(x => x.Id).ToArrayAsync(ct))
                await runtimePreparation.PrepareAsync(question, command.Now, ct);
        return result;
    }

    private async Task<LiveSoloQuestionGroup?> SuitableGroupAsync(LiveSoloMatch match, Guid? requestedId, DateTimeOffset now, CancellationToken ct)
    {
        var groups = await db.LiveSoloQuestionGroups.AsNoTracking().Include(x => x.Items)
            .Where(x => x.CompetitionId == match.CompetitionId && (requestedId == null || x.Id == requestedId))
            .OrderBy(x => x.Reserve).ThenBy(x => x.Position).ToArrayAsync(ct);
        var teams = match.Slots.Where(x => x.TeamId is not null).Select(x => x.TeamId!.Value).ToArray();
        var users = match.Roster.Select(x => x.UserId).ToArray();
        var previousEntries = await db.LiveSoloRoundQuestions.AsNoTracking().Where(x => x.OpenedAt != null)
            .Join(db.LiveSoloRounds, x => x.RoundId, x => x.Id, (question, round) => new { Question = question, Round = round })
            .Join(db.LiveSoloMatches, x => x.Round.MatchId, x => x.Id, (x, previous) => new { x.Question, Previous = previous })
            .Where(x => x.Previous.CompetitionId == match.CompetitionId && (x.Previous.Slots.Any(s => s.TeamId != null && teams.Contains(s.TeamId.Value))
                || x.Previous.Roster.Any(r => users.Contains(r.UserId))))
            .Select(x => x.Question.CompetitionChallengeId).Distinct().ToArrayAsync(ct);
        var directory = await db.CompetitionChallenges.AsNoTracking().Where(x => x.CompetitionId == match.CompetitionId)
            .Select(x => new { x.Id, x.ChallengeId }).ToArrayAsync(ct);
        var provenance = await db.LiveSoloChallengeSources.AsNoTracking().Where(x => directory.Select(d => d.ChallengeId).Contains(x.ChallengeId))
            .ToDictionaryAsync(x => x.ChallengeId, x => x.CanonicalChallengeId, ct);
        Guid Canonical(Guid template) => provenance.GetValueOrDefault(template, template);
        var priorCanonical = directory.Where(x => previousEntries.Contains(x.Id)).Select(x => Canonical(x.ChallengeId)).ToHashSet();
        var publicCanonical = match.StartedAt is null ? await db.LiveSoloQuestionExposures.AsNoTracking()
            .Where(x => x.CompetitionId == match.CompetitionId && x.PublicAt <= now).Select(x => x.CanonicalChallengeId).ToArrayAsync(ct) : [];
        var usedGroups = await db.LiveSoloRounds.Where(x => x.MatchId == match.Id).Select(x => x.QuestionGroupId).ToArrayAsync(ct);
        return groups.FirstOrDefault(group => !usedGroups.Contains(group.Id) && group.Items.All(item => directory.Any(d => d.Id == item.CompetitionChallengeId
            && !priorCanonical.Contains(Canonical(d.ChallengeId)) && !publicCanonical.Contains(Canonical(d.ChallengeId)))));
    }

    public async Task<LiveSoloRoundResult> StartCountdownAsync(StartLiveSoloCountdown command, CancellationToken ct)
    {
        if (!await ActiveAsync(command.ActorId, ct) || !await authorizer.CanJudgeAsync(command.ActorId, command.CompetitionId, ct)) return new(null, LiveSoloFailure.Forbidden);
        if (!await db.LiveSoloRounds.AnyAsync(x => x.Id == command.RoundId && x.MatchId == command.MatchId
            && db.LiveSoloMatches.Any(m => m.Id == x.MatchId && m.CompetitionId == command.CompetitionId), ct)) return new(null, LiveSoloFailure.NotFound);
        var readiness = await media.CheckAsync(ct);
        if (!readiness.Configured || !readiness.Available || !readiness.EgressAvailable) return new(null, LiveSoloFailure.MediaUnavailable);
        var first = await db.LiveSoloRoundQuestions.AsNoTracking().Where(x => x.RoundId == command.RoundId && x.Position == 0).Select(x => (Guid?)x.Id).SingleOrDefaultAsync(ct);
        if (first is null) return new(null, LiveSoloFailure.NotFound);
        if (await runtimePreparation.PrepareAsync(first.Value, command.Now, ct) is { } failed) return new(null, failed);
        var session = await db.LiveSoloMediaSessions.AsNoTracking().Include(x => x.Participants)
            .Where(x => x.MatchId == command.MatchId && x.State == LiveSoloMediaState.Ready).OrderByDescending(x => x.CreatedAt).FirstOrDefaultAsync(ct);
        if (session is null) return new(null, LiveSoloFailure.MediaUnavailable);
        var observed = await media.ObserveAsync(session.RoomIdentity, ct);
        return await TransactionAsync(async () =>
        {
            var match = await MatchAsync(command.CompetitionId, command.MatchId, ct);
            var competition = await db.Competitions.AsNoTracking().SingleOrDefaultAsync(x => x.Id == command.CompetitionId, ct);
            var round = await db.LiveSoloRounds.Include(x => x.Questions).Include(x => x.Pauses).SingleOrDefaultAsync(x => x.Id == command.RoundId && x.MatchId == command.MatchId, ct);
            if (match is null || round is null || match.CurrentRoundId != round.Id) return new LiveSoloRoundResult(null, LiveSoloFailure.NotFound);
            if (round.ConcurrencyStamp != command.ExpectedStamp) return new(null, LiveSoloFailure.Conflict);
            if (match.State != LiveSoloMatchState.Preparing || round.State != LiveSoloRoundState.Preparing
                || competition?.Status != CompetitionStatus.Running || match.Slots.Any(x => x.ReadyConfirmedAt is null)
                || round.Questions.Single(x => x.Position == 0).Readiness != LiveSoloQuestionReadiness.Ready) return new(null, LiveSoloFailure.NotReady);
            var config = (LiveSoloCompetitionModeConfiguration)competition.ModeConfiguration!;
            if (!config.Enabled) return new(null, LiveSoloFailure.Disabled);
            if (!await RosterEligibleAsync(match, ct)) return new(null, LiveSoloFailure.NotReady);
            if (await db.LiveSoloMatches.CountAsync(x => x.CompetitionId == match.CompetitionId && x.Id != match.Id
                && (x.State == LiveSoloMatchState.Countdown || x.StartedAt != null
                    && x.State != LiveSoloMatchState.Completed && x.State != LiveSoloMatchState.Canceled), ct) >= config.MaximumConcurrentMatches)
                return new(null, LiveSoloFailure.NotReady);
            if (match.Roster.Any(member => !session.Participants.Any(p => p.UserId == member.UserId
                && observed.Screens.Any(screen => screen.Identity == p.Identity && screen.State == LiveSoloScreenState.Sharing)))) return new(null, LiveSoloFailure.NotReady);
            // Exposure can change while both sides are preparing. Recheck before a new Match starts.
            if (match.StartedAt is null && !await NoNewPublicExposureAsync(match, round, command.Now, ct)) return new(null, LiveSoloFailure.NoSuitableQuestionGroup);
            match.State = LiveSoloMatchState.Countdown; round.State = LiveSoloRoundState.Countdown; round.CountdownAt = command.Now; round.TimelineRevision++;
            await messages.ScheduleAsync(new NoCTF.Application.LiveSolo.Rounds.AdvanceLiveSoloRound(round.Id, round.TimelineRevision,
                command.Now.AddSeconds(round.CountdownSeconds)), command.Now.AddSeconds(round.CountdownSeconds));
            await db.SaveChangesAsync(ct); return new(Round(round, command.Now));
        }, () => new(null, LiveSoloFailure.Conflict), ct);
    }

    private async Task<bool> NoNewPublicExposureAsync(LiveSoloMatch match, LiveSoloRound round, DateTimeOffset now, CancellationToken ct)
    {
        var templates = await db.CompetitionChallenges.Where(x => round.Questions.Select(q => q.CompetitionChallengeId).Contains(x.Id)).Select(x => x.ChallengeId).ToArrayAsync(ct);
        var canonical = await db.LiveSoloChallengeSources.Where(x => templates.Contains(x.ChallengeId)).ToDictionaryAsync(x => x.ChallengeId, x => x.CanonicalChallengeId, ct);
        var ids = templates.Select(id => canonical.GetValueOrDefault(id, id)).ToArray();
        return !await db.LiveSoloQuestionExposures.AnyAsync(x => x.CompetitionId == match.CompetitionId && ids.Contains(x.CanonicalChallengeId) && x.PublicAt <= now, ct);
    }
    private async Task<bool> RosterEligibleAsync(LiveSoloMatch match, CancellationToken ct)
    {
        var teamIds = match.Slots.Where(x => x.TeamId != null).Select(x => x.TeamId!.Value).ToArray();
        var members = match.Roster.Select(x => x.UserId).ToArray();
        return teamIds.Length == 2 && members.Length != 0
            && await db.Teams.CountAsync(x => teamIds.Contains(x.Id) && x.CompetitionId == match.CompetitionId
                && !x.IsBanned && x.RegistrationStatus == NoCTF.Domain.Teams.TeamRegistrationStatus.Approved, ct) == 2
            && await db.Users.CountAsync(x => members.Contains(x.Id) && x.AccountStatus == NoCTF.Domain.Identity.UserAccountStatus.Active, ct) == members.Length
            && await db.Set<LiveSoloRosterMember>().Where(x => x.MatchId == match.Id)
                .Join(db.Set<NoCTF.Domain.Teams.TeamMember>(), x => new { x.TeamId, x.UserId }, x => new { x.TeamId, x.UserId }, (roster, _) => roster)
                .CountAsync(ct) == members.Length;
    }
    private static LiveSoloRoundView Round(LiveSoloRound round, DateTimeOffset now) => new(round.Id, round.MatchId, round.Number, round.Replay,
        round.State, round.ConcurrencyStamp, round.TimelineRevision, round.CountdownAt, round.StartedAt, round.LimitSeconds,
        round.StartedAt is { } started ? Math.Min(round.LimitSeconds * 1000L,
            (long)LiveSoloActiveClock.Elapsed(started, round.EndedAt is { } ended && ended < now ? ended : now, round.Pauses).TotalMilliseconds) : 0,
        LiveSoloActiveClock.Paused(round.Pauses), round.WinnerTeamId, round.WinningGameplayFactId);

    public async Task<LiveSoloRoundView?> FindRoundAsync(Guid competitionId, Guid matchId, Guid roundId, Guid actorId, bool staff, DateTimeOffset now, CancellationToken ct)
    {
        if (await FindAsync(competitionId, matchId, actorId, staff, now, ct) is null) return null;
        var round = await db.LiveSoloRounds.AsNoTracking().Include(x => x.Pauses).SingleOrDefaultAsync(x => x.Id == roundId && x.MatchId == matchId, ct);
        if (round is not null) await SynchronizeCompetitionPausesAsync(round, competitionId, now, false, ct);
        return round is null ? null : Round(round, now);
    }
}
