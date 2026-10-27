using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using NoCTF.Application.LiveSolo.Matches;
using NoCTF.Application.LiveSolo.Media;
using NoCTF.Application.LiveSolo.Rounds;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.LiveSolo;

namespace NoCTF.Infrastructure.LiveSolo.Matches;

public sealed partial class LiveSoloMatchStore : ILiveSoloDirectionStore
{
    public async Task<IReadOnlyList<LiveSoloDirectionPresetView>?> PresetsAsync(Guid competitionId, Guid actorId, CancellationToken ct)
    {
        if (!await ActiveAsync(actorId, ct) || !await authorizer.CanObserveAsync(actorId, competitionId, ct)) return null;
        return await db.Set<LiveSoloDirectionPreset>().AsNoTracking().Where(x => x.CompetitionId == competitionId)
            .Join(db.LiveSoloQuestionGroups, x => x.QuestionGroupId, x => x.Id, (preset, group) => new { Preset = preset, Group = group })
            .OrderBy(x => x.Preset.Name).ThenBy(x => x.Preset.Id)
            .Select(x => new LiveSoloDirectionPresetView(x.Preset.Id, x.Preset.Name, x.Preset.Direction, x.Group.Id, x.Group.Name)).ToArrayAsync(ct);
    }

    public Task<LiveSoloDirectionPresetResult> SavePresetAsync(Guid competitionId, Guid actorId, LiveSoloDirectionPresetInput input, CancellationToken ct) =>
        TransactionAsync(async () =>
        {
            if (!await ActiveAsync(actorId, ct) || !await authorizer.CanModerateAsync(actorId, competitionId, ct))
                return new LiveSoloDirectionPresetResult(null, LiveSoloFailure.Forbidden);
            if ((input.Name?.Trim().Length ?? 0) > 160 || string.IsNullOrWhiteSpace(input.Direction) || input.Direction.Trim().Length > 96)
                return new(null, LiveSoloFailure.InvalidConfiguration);
            var group = await db.LiveSoloQuestionGroups.AsNoTracking().SingleOrDefaultAsync(x => x.Id == input.QuestionGroupId && x.CompetitionId == competitionId, ct);
            if (group is null) return new(null, LiveSoloFailure.QuestionGroupNotFound);
            var preset = input.Id is Guid id ? await db.Set<LiveSoloDirectionPreset>().SingleOrDefaultAsync(x => x.Id == id && x.CompetitionId == competitionId, ct)
                : new LiveSoloDirectionPreset { Id = Guid.CreateVersion7(), CompetitionId = competitionId };
            if (preset is null) return new(null, LiveSoloFailure.NotFound);
            preset.Direction = input.Direction.Trim(); preset.Name = string.IsNullOrWhiteSpace(input.Name) ? preset.Direction : input.Name.Trim(); preset.QuestionGroupId = group.Id;
            preset.ConcurrencyStamp = Guid.NewGuid();
            if (input.Id is null) db.Add(preset);
            await db.SaveChangesAsync(ct);
            return new(new(preset.Id, preset.Name, preset.Direction, group.Id, group.Name));
        }, () => new(null, LiveSoloFailure.Conflict), ct);

    public Task<LiveSoloFailure?> DeletePresetAsync(Guid competitionId, Guid presetId, Guid actorId, CancellationToken ct) =>
        TransactionAsync<LiveSoloFailure?>(async () =>
        {
            if (!await ActiveAsync(actorId, ct) || !await authorizer.CanModerateAsync(actorId, competitionId, ct)) return LiveSoloFailure.Forbidden;
            var preset = await db.Set<LiveSoloDirectionPreset>().SingleOrDefaultAsync(x => x.Id == presetId && x.CompetitionId == competitionId, ct);
            if (preset is null) return LiveSoloFailure.NotFound;
            db.Remove(preset); await db.SaveChangesAsync(ct); return null;
        }, () => LiveSoloFailure.Conflict, ct);

    public async Task<LiveSoloDirectionState?> DirectionsAsync(Guid competitionId, Guid matchId, Guid actorId, bool staff, CancellationToken ct)
    {
        if (await FindAsync(competitionId, matchId, actorId, staff, (clock ?? TimeProvider.System).GetUtcNow(), ct) is null) return null;
        var match = (await MatchAsync(competitionId, matchId, ct))!;
        var judge = staff && await authorizer.CanJudgeAsync(actorId, competitionId, ct);
        var manager = staff && await authorizer.CanModerateAsync(actorId, competitionId, ct);
        var teamId = staff ? null : await TeamAsync(competitionId, actorId, ct);
        var round = match.CurrentRoundId is Guid id ? await db.LiveSoloRounds.AsNoTracking().SingleAsync(x => x.Id == id, ct) : null;
        var live = round?.State == LiveSoloRoundState.Running && match.State is LiveSoloMatchState.Running or LiveSoloMatchState.Paused
            && match.PendingCorrectionId == null && match.SupersededAt == null;
        var requests = await db.Set<LiveSoloRescueRequest>().AsNoTracking().Where(x => x.MatchId == matchId && (staff || x.TeamId == teamId))
            .Join(db.Users, x => x.UserId, x => x.Id, (request, user) => new { Request = request, user.UserName })
            .OrderByDescending(x => x.Request.RequestedAt).ThenBy(x => x.Request.Id)
            .Select(x => new LiveSoloRescueView(x.Request.Id, x.Request.RoundId, x.Request.TeamId, x.Request.UserId,
                x.UserName, x.Request.Reason, x.Request.State, x.Request.RequestedAt, x.Request.JudgeUserId, x.Request.DecisionReason)).ToArrayAsync(ct);
        var rounds = await db.LiveSoloRounds.AsNoTracking().Where(x => x.MatchId == matchId).ToArrayAsync(ct);
        var next = LiveSoloDirectionPoolPolicy.RoundNumber(rounds);
        var used = await PreviouslyDrawnGroupsAsync(match, ct);
        return new(match.DirectionPool.OrderBy(x => x.Position).Select(x => new LiveSoloDirectionView(x.Id, staff ? x.Name : x.Direction, x.Direction, x.RoundNumber, x.DrawnAt,
                staff ? x.QuestionGroupId : null, x.DrawnAt == null && !used.Contains(x.QuestionGroupId))).ToArray(), requests,
            manager && LiveSoloDirectionPoolPolicy.CanConfigure(match),
            judge && match.State == LiveSoloMatchState.Preparing && match.PendingCorrectionId == null && match.SupersededAt == null
                && match.DirectionPool.Any(x => x.DrawnAt == null && !used.Contains(x.QuestionGroupId)) && match.DirectionPool.All(x => x.RoundNumber != next)
                && rounds.All(x => x.State is LiveSoloRoundState.Won or LiveSoloRoundState.Canceled or LiveSoloRoundState.TimedOut),
            !staff && live && teamId != null && match.Slots.Any(x => x.TeamId == teamId),
            judge && live, manager);
    }

    private async Task<Guid[]> PreviouslyDrawnGroupsAsync(LiveSoloMatch match, CancellationToken ct)
    {
        var teams = match.Slots.Where(x => x.TeamId != null).Select(x => x.TeamId!.Value).ToArray();
        var drawn = await db.Set<LiveSoloMatchDirection>().AsNoTracking().Where(x => x.DrawnAt != null && x.MatchId != match.Id)
            .Join(db.LiveSoloMatches.IgnoreQueryFilters(), x => x.MatchId, x => x.Id, (draw, other) => new { draw.QuestionGroupId, Match = other })
            .Where(x => x.Match.CompetitionId == match.CompetitionId && x.Match.Slots.Any(s => s.TeamId != null && teams.Contains(s.TeamId.Value)))
            .Select(x => x.QuestionGroupId).ToArrayAsync(ct);
        var played = await db.LiveSoloRounds.AsNoTracking().Where(x => x.MatchId != match.Id || x.DirectionDrawId == null)
            .Join(db.LiveSoloMatches.IgnoreQueryFilters(), x => x.MatchId, x => x.Id, (round, other) => new { round.QuestionGroupId, Match = other })
            .Where(x => x.Match.CompetitionId == match.CompetitionId && x.Match.Slots.Any(s => s.TeamId != null && teams.Contains(s.TeamId.Value)))
            .Select(x => x.QuestionGroupId).ToArrayAsync(ct);
        return drawn.Concat(played).Distinct().ToArray();
    }

    public Task<LiveSoloMatchResult> ConfigurePoolAsync(Guid competitionId, Guid matchId, Guid actorId, IReadOnlyList<Guid> presetIds, CancellationToken ct) =>
        TransactionAsync(async () =>
        {
            if (!await ActiveAsync(actorId, ct) || !await authorizer.CanModerateAsync(actorId, competitionId, ct)) return new LiveSoloMatchResult(null, LiveSoloFailure.Forbidden);
            var match = await MatchAsync(competitionId, matchId, ct);
            if (match is null) return new(null, LiveSoloFailure.NotFound);
            if (!LiveSoloDirectionPoolPolicy.CanConfigure(match)) return new(null, LiveSoloFailure.NotReady);
            if (presetIds.Distinct().Count() != presetIds.Count)
                return new(null, LiveSoloFailure.InvalidConfiguration);
            var presets = await db.Set<LiveSoloDirectionPreset>().AsNoTracking().Where(x => x.CompetitionId == competitionId && presetIds.Contains(x.Id)).ToArrayAsync(ct);
            if (presets.Length != presetIds.Count || presets.Select(x => x.QuestionGroupId).Distinct().Count() != presets.Length)
                return new(null, LiveSoloFailure.InvalidConfiguration);
            var drawn = match.DirectionPool.Where(x => x.DrawnAt != null).ToArray();
            var pending = match.DirectionPool.Where(x => x.DrawnAt == null).ToArray();
            db.RemoveRange(pending);
            foreach (var entry in pending) match.DirectionPool.Remove(entry);
            await db.SaveChangesAsync(ct);
            var position = drawn.Select(x => x.Position).DefaultIfEmpty(-1).Max() + 1;
            var additions = presetIds.Select(id => presets.Single(x => x.Id == id))
                .Where(preset => !drawn.Any(x => x.QuestionGroupId == preset.QuestionGroupId)).Select(preset =>
            {
                return new LiveSoloMatchDirection { Id = Guid.CreateVersion7(), MatchId = match.Id, Position = position++,
                    Name = preset.Name, Direction = preset.Direction, QuestionGroupId = preset.QuestionGroupId };
            }).ToList();
            match.DirectionPool.AddRange(additions); db.AddRange(additions);
            match.ConcurrencyStamp = Guid.NewGuid(); await db.SaveChangesAsync(ct); return new(await MapAsync(match, ct));
        }, () => new(null, LiveSoloFailure.Conflict), ct);

    public Task<LiveSoloMatchResult> DrawAsync(Guid competitionId, Guid matchId, Guid actorId, DateTimeOffset now, CancellationToken ct) =>
        TransactionAsync(async () =>
        {
            if (!await ActiveAsync(actorId, ct) || !await authorizer.CanJudgeAsync(actorId, competitionId, ct)) return new LiveSoloMatchResult(null, LiveSoloFailure.Forbidden);
            var match = await MatchAsync(competitionId, matchId, ct);
            if (match is null) return new(null, LiveSoloFailure.NotFound);
            if (match.State != LiveSoloMatchState.Preparing || match.PendingCorrectionId != null || match.SupersededAt != null
                || !await db.Set<LiveSoloCompetitionModeConfiguration>().AnyAsync(x => x.CompetitionId == competitionId && x.Enabled, ct)
                || await db.Competitions.AnyAsync(x => x.Id == competitionId && x.Status == CompetitionStatus.Finished, ct)) return new(null, LiveSoloFailure.NotReady);
            var rounds = await db.LiveSoloRounds.Where(x => x.MatchId == matchId).ToArrayAsync(ct);
            if (rounds.Any(x => x.State is not (LiveSoloRoundState.Won or LiveSoloRoundState.Canceled or LiveSoloRoundState.TimedOut))) return new(null, LiveSoloFailure.NotReady);
            var number = LiveSoloDirectionPoolPolicy.RoundNumber(rounds);
            if (match.DirectionPool.Any(x => x.RoundNumber == number)) return new(await MapAsync(match, ct));
            var used = await PreviouslyDrawnGroupsAsync(match, ct);
            var remaining = match.DirectionPool.Where(x => x.DrawnAt == null && !used.Contains(x.QuestionGroupId)).OrderBy(x => x.Position).ToArray();
            if (remaining.Length == 0) return new(null, LiveSoloFailure.NoSuitableQuestionGroup);
            var selected = remaining[RandomNumberGenerator.GetInt32(remaining.Length)];
            selected.RoundNumber = number; selected.DrawnAt = now;
            db.RemoveRange(match.Roster); match.Roster.Clear();
            foreach (var slot in match.Slots) { slot.RosterLockedAt = null; slot.ReadyConfirmedAt = null; }
            await RetireMediaAsync(match, now, ct);
            match.ConcurrencyStamp = Guid.NewGuid(); await db.SaveChangesAsync(ct); return new(await MapAsync(match, ct));
        }, () => new(null, LiveSoloFailure.Conflict), ct);

    private async Task RetireMediaAsync(LiveSoloMatch match, DateTimeOffset now, CancellationToken ct)
    {
        if (match.CurrentMediaSessionId is not Guid id) return;
        var session = await db.LiveSoloMediaSessions.SingleAsync(x => x.Id == id, ct);
        session.State = LiveSoloMediaState.Stopping; match.CurrentMediaSessionId = null;
        foreach (var grant in await db.LiveSoloMediaGrants.Where(x => x.MediaSessionId == id && x.RevokedAt == null).ToArrayAsync(ct)) grant.RevokedAt = now;
        await messages.PublishAsync(new RefreshLiveSoloMedia(id, session.RoomIdentity));
    }
}
