using Microsoft.EntityFrameworkCore;
using NoCTF.Application.LiveSolo.Adjudication;
using NoCTF.Application.LiveSolo.Media;
using NoCTF.Application.LiveSolo.Rounds;
using NoCTF.Domain.LiveSolo;
using NoCTF.Domain.Gameplay;
using NoCTF.Application.GameplayFacts.Processing;
using NoCTF.Application.Messaging;

namespace NoCTF.Infrastructure.LiveSolo.Matches;

public sealed partial class LiveSoloMatchStore
{
    private Task<List<LiveSoloMatch>> CorrectionGraphAsync(Guid competitionId, CancellationToken ct) => db.LiveSoloMatches
        .Include(x => x.Slots).Include(x => x.Rounds).ThenInclude(x => x.Pauses)
        .Where(x => x.CompetitionId == competitionId && x.SupersededAt == null).ToListAsync(ct);
    public async Task<LiveSoloCorrectionPreview?> PreviewAsync(LiveSoloCorrectionProposal proposal, CancellationToken ct)
    {
        if (!await ActiveAsync(proposal.ActorId, ct) || !await authorizer.CanModerateAsync(proposal.ActorId, proposal.CompetitionId, ct)) return null;
        var graph = await CorrectionGraphAsync(proposal.CompetitionId, ct);
        var source = graph.SingleOrDefault(x => x.Id == proposal.MatchId);
        if (source is null || !LiveSoloCorrectionPolicy.CanPropose(source, proposal)) return null;
        var downstream = source.WinnerTeamId == proposal.WinnerTeamId ? [] : LiveSoloCorrectionPolicy.Downstream(source.Id, graph);
        if (downstream.Any(x => x.PendingCorrectionId != null)) return null;
        return new(LiveSoloCorrectionPolicy.PreviewId(proposal, downstream.Prepend(source)), source.ConcurrencyStamp,
            source.WinnerTeamId!.Value, source.LeftWins, source.RightWins, await ImpactsAsync(downstream, null, ct));
    }
    public async Task<LiveSoloCorrectionView?> ReadCorrectionAsync(Guid competitionId, Guid matchId, Guid correctionId, Guid actorId, CancellationToken ct)
    {
        if (!await ActiveAsync(actorId, ct) || !await authorizer.CanObserveAsync(actorId, competitionId, ct)) return null;
        var correction = await db.Set<LiveSoloResultCorrection>().Include(x => x.Matches)
            .SingleOrDefaultAsync(x => x.Id == correctionId && x.CompetitionId == competitionId && x.MatchId == matchId, ct);
        return correction is null ? null : await CorrectionViewAsync(correction, ct);
    }
    public async Task<IReadOnlyList<LiveSoloCorrectionView>?> ListCorrectionsAsync(Guid competitionId, Guid matchId, Guid actorId, CancellationToken ct)
    {
        if (!await ActiveAsync(actorId, ct) || !await authorizer.CanObserveAsync(actorId, competitionId, ct)
            || !await db.LiveSoloMatches.AnyAsync(x => x.Id == matchId && x.CompetitionId == competitionId, ct)) return null;
        var records = await db.Set<LiveSoloResultCorrection>().Include(x => x.Matches).Where(x => x.CompetitionId == competitionId && x.MatchId == matchId)
            .OrderByDescending(x => x.CreatedAt).Take(100).ToArrayAsync(ct);
        var matchIds=records.SelectMany(x=>x.Matches).Select(x=>x.MatchId).Distinct().ToArray();
        var matches=await db.LiveSoloMatches.Include(x=>x.Slots).Where(x=>matchIds.Contains(x.Id)).ToArrayAsync(ct);
        var actorIds=records.SelectMany(x=>new Guid?[]{x.ActorUserId,x.ResolvedByUserId}).Where(x=>x!=null).Select(x=>x!.Value).Distinct().ToArray();
        var actors=await db.Users.Where(x=>actorIds.Contains(x.Id)).ToDictionaryAsync(x=>x.Id,x=>x.UserName,ct);
        var impacts=await ImpactsAsync(matches,null,ct);
        return records.Select(record=>CorrectionView(record,record.Matches.Select(node=>{
            var impact=impacts.Single(x=>x.MatchId==node.MatchId);
            return impact with {RequiresReplay=node.WasStarted,ReplacementMatchId=node.ReplacementMatchId};
        }).ToArray(),actors)).ToArray();
    }
    public Task<LiveSoloCorrectionResult> BeginCorrectionAsync(BeginLiveSoloCorrection command, CancellationToken ct) => TransactionAsync(async () =>
    {
        var proposal = command.Proposal;
        if (!await ActiveAsync(proposal.ActorId, ct) || !await authorizer.CanModerateAsync(proposal.ActorId, proposal.CompetitionId, ct))
            return new LiveSoloCorrectionResult(null, LiveSoloFailure.Forbidden);
        var graph = await CorrectionGraphAsync(proposal.CompetitionId, ct);
        var source = graph.SingleOrDefault(x => x.Id == proposal.MatchId);
        if (source is null || !LiveSoloCorrectionPolicy.CanPropose(source, proposal)) return new(null, LiveSoloFailure.InvalidConfiguration);
        var downstream = source.WinnerTeamId == proposal.WinnerTeamId ? [] : LiveSoloCorrectionPolicy.Downstream(source.Id, graph);
        if (downstream.Any(x => x.PendingCorrectionId != null)
            || LiveSoloCorrectionPolicy.PreviewId(proposal, downstream.Prepend(source)) != command.PreviewId) return new(null, LiveSoloFailure.Conflict);
        var now = (clock ?? TimeProvider.System).GetUtcNow();
        var correction = new LiveSoloResultCorrection { Id = Guid.CreateVersion7(now), CompetitionId = proposal.CompetitionId, MatchId = source.Id,
            ActorUserId = proposal.ActorId, WinnerTeamId = proposal.WinnerTeamId, LeftWins = proposal.LeftWins, RightWins = proposal.RightWins,
            PreviousWinnerTeamId = source.WinnerTeamId!.Value, PreviousLeftWins = source.LeftWins, PreviousRightWins = source.RightWins,
            Reason = command.Reason, CreatedAt = now };
        foreach (var match in downstream)
            correction.Matches.Add(new() { CorrectionId = correction.Id, MatchId = match.Id, PreviousState = match.State, WasStarted = RequiresReplay(match) });
        db.Add(correction); await db.SaveChangesAsync(ct);
        foreach (var match in downstream.Prepend(source))
        {
            match.PendingCorrectionId = correction.Id; match.ConcurrencyStamp = Guid.NewGuid();
            if (match.Id == source.Id) continue;
            if (match.CurrentRoundId is Guid id && match.Rounds.SingleOrDefault(x => x.Id == id) is { } round
                && round.State is LiveSoloRoundState.Countdown or LiveSoloRoundState.Running or LiveSoloRoundState.ConfirmingResult)
            {
                db.Add(new LiveSoloPauseInterval { Id = Guid.CreateVersion7(now), RoundId = round.Id, Source = LiveSoloPauseSource.ResultCorrection, StartedAt = now });
                round.TimelineRevision++; round.ConcurrencyStamp = Guid.NewGuid();
                match.State = LiveSoloMatchState.Paused;
            }
        }
        await db.SaveChangesAsync(ct); return new(await CorrectionViewAsync(correction, ct));
    }, () => new(null, LiveSoloFailure.Conflict), ct);

    public async Task<LiveSoloCorrectionResult> ResolveCorrectionAsync(ResolveLiveSoloCorrection command, CancellationToken ct)
    {
        var result = await TransactionAsync(async () =>
        {
            if (!await ActiveAsync(command.ActorId, ct) || !await authorizer.CanModerateAsync(command.ActorId, command.CompetitionId, ct))
                return new LiveSoloCorrectionResult(null, LiveSoloFailure.Forbidden);
            var correction = await db.Set<LiveSoloResultCorrection>().Include(x => x.Matches).SingleOrDefaultAsync(x => x.Id == command.CorrectionId
                && x.CompetitionId == command.CompetitionId && x.MatchId == command.MatchId, ct);
            if (correction is null) return new(null, LiveSoloFailure.NotFound);
            if (correction.State != LiveSoloCorrectionState.Pending || correction.ConcurrencyStamp != command.ExpectedStamp) return new(null, LiveSoloFailure.Conflict);
            var graph = await CorrectionGraphAsync(command.CompetitionId, ct);
            var source = graph.SingleOrDefault(x => x.Id == command.MatchId);
            var downstream = graph.Where(x => correction.Matches.Any(node => node.MatchId == x.Id)).ToArray();
            if (source?.PendingCorrectionId != correction.Id || downstream.Length != correction.Matches.Count
                || source.State != LiveSoloMatchState.Completed || source.WinnerTeamId != correction.PreviousWinnerTeamId
                || source.LeftWins != correction.PreviousLeftWins || source.RightWins != correction.PreviousRightWins
                || downstream.Any(x => x.PendingCorrectionId != correction.Id)) return new(null, LiveSoloFailure.Conflict);
            var required = correction.Matches.Where(x => x.WasStarted).Select(x => x.MatchId).ToHashSet();
            if (command.Apply && (!required.SetEquals(command.Replays.Select(x => x.MatchId))
                || command.Replays.Any(x => downstream.Single(m => m.Id == x.MatchId).ConcurrencyStamp != x.ConcurrencyStamp)))
                return new(null, LiveSoloFailure.Conflict);
            var now = (clock ?? TimeProvider.System).GetUtcNow();
            if (command.Apply)
            {
                source.WinnerTeamId = correction.WinnerTeamId; source.LeftWins = correction.LeftWins; source.RightWins = correction.RightWins;
                source.AdjudicationReason = command.Reason;
                var replacements = downstream.ToDictionary(x => x.Id, x => new LiveSoloMatch {
                    Id = Guid.CreateVersion7(now), CompetitionId = x.CompetitionId, Lane = x.Lane, Stage = x.Stage, Position = x.Position,
                    BracketGeneration = checked(x.BracketGeneration + 1), RequiredWins = x.RequiredWins, Conditional = x.Conditional,
                    State = LiveSoloMatchState.AwaitingOpponents, CreatedAt = now });
                foreach (var old in downstream)
                {
                    var replacement = replacements[old.Id];
                    foreach (var slot in old.Slots)
                        replacement.Slots.Add(new() { MatchId = replacement.Id, Side = slot.Side, Source = slot.Source, Seed = slot.Seed,
                            SourceMatchId = slot.SourceMatchId is Guid parent && replacements.TryGetValue(parent, out var mapped) ? mapped.Id : slot.SourceMatchId,
                            TeamId = slot.SourceMatchId is null ? slot.TeamId : null, Resolved = slot.SourceMatchId is null && slot.Resolved });
                    db.Add(replacement);
                    await ReleaseActiveTeamSlotsAsync(old.Id, ct);
                    foreach (var round in old.Rounds)
                    {
                        round.State = LiveSoloRoundState.Canceled; round.EndedAt ??= now; round.AdjudicationReason = command.Reason;
                        round.TimelineRevision++; round.ConcurrencyStamp = Guid.NewGuid();
                        foreach (var pause in round.Pauses.Where(x => x.EndedAt is null)) pause.EndedAt = now;
                        var pending = await db.GameplayFacts.Where(x => x.State != GameplayFactState.Completed
                            && db.LiveSoloSubmissions.Any(s => s.RoundId == round.Id && s.GameplayFactId == x.Id)).ToArrayAsync(ct);
                        foreach (var fact in pending)
                        { fact.State = GameplayFactState.Completed; fact.Result = GameplayFactResult.Rejected; fact.FailureCode = GameplayFactFailureCode.RoundOutOfRange;
                            fact.UpdatedAt = now; await messages.PublishAsync(new GameplayFactStateChanged(fact.Id, fact.State)); }
                    }
                    if (old.CurrentMediaSessionId is Guid mediaId)
                    {
                        var session = await db.LiveSoloMediaSessions.SingleAsync(x => x.Id == mediaId, ct);
                        session.State = LiveSoloMediaState.Stopping;
                        await messages.PublishAsync(new RefreshLiveSoloMedia(session.Id, session.RoomIdentity));
                    }
                    old.State = LiveSoloMatchState.Canceled; old.SupersededAt = now; old.ReplacementMatchId = replacement.Id;
                    old.AdjudicationReason = command.Reason;
                    old.PendingCorrectionId = null; old.ConcurrencyStamp = Guid.NewGuid();
                }
                // Release old unique team slots before normal graph resolution allocates new ones.
                await db.SaveChangesAsync(ct); source.PendingCorrectionId = null;
                foreach (var node in correction.Matches) node.ReplacementMatchId = replacements[node.MatchId].Id;
                await AdvanceBracketAsync(source, now, ct);
            }
            else
            {
                foreach (var match in downstream)
                {
                    foreach (var round in match.Rounds.Where(x => x.Pauses.Any(p => p.Source == LiveSoloPauseSource.ResultCorrection && p.EndedAt is null)))
                    {
                        foreach (var pause in round.Pauses.Where(x => x.Source == LiveSoloPauseSource.ResultCorrection && x.EndedAt is null)) pause.EndedAt = now;
                        round.TimelineRevision++; round.ConcurrencyStamp = Guid.NewGuid();
                        if (LiveSoloRoundSchedulePolicy.NextWakeup(round, now) is { } next)
                            await messages.ScheduleAsync(new AdvanceLiveSoloRound(round.Id, round.TimelineRevision, next), next);
                    }
                    match.State = correction.Matches.Single(x => x.MatchId == match.Id).PreviousState;
                    match.PendingCorrectionId = null; match.ConcurrencyStamp = Guid.NewGuid();
                }
                source.PendingCorrectionId = null;
            }
            source.ConcurrencyStamp = Guid.NewGuid();
            correction.State = command.Apply ? LiveSoloCorrectionState.Applied : LiveSoloCorrectionState.Canceled;
            correction.ResolvedAt = now; correction.ResolvedByUserId = command.ActorId; correction.ResolutionReason = command.Reason;
            await db.SaveChangesAsync(ct); return new(await CorrectionViewAsync(correction, ct));
        }, () => new(null, LiveSoloFailure.Conflict), ct);
        if (result.Failure is null && command.Apply && result.Correction is { } applied)
            foreach (var roundId in await db.LiveSoloRounds.Where(x => applied.Downstream.Select(m => m.MatchId).Contains(x.MatchId)).Select(x => x.Id).ToArrayAsync(ct))
                await runtimePreparation.StopRoundAsync(roundId, (clock ?? TimeProvider.System).GetUtcNow(), ct);
        return result;
    }
    private static bool RequiresReplay(LiveSoloMatch match) => match.StartedAt is not null || match.Rounds.Any(x => x.CountdownAt is not null);
    private async Task<IReadOnlyList<LiveSoloCorrectionImpact>> ImpactsAsync(IEnumerable<LiveSoloMatch> matches,
        IReadOnlyList<LiveSoloCorrectionMatch>? nodes, CancellationToken ct)
    {
        var list = matches.ToArray(); var ids = list.SelectMany(x => x.Slots).Where(x => x.TeamId != null).Select(x => x.TeamId!.Value).Distinct().ToArray();
        var names = await db.Teams.IgnoreQueryFilters().Where(x => ids.Contains(x.Id)).ToDictionaryAsync(x => x.Id, x => x.Name, ct);
        return list.Select(x => { var left=x.Slots.Single(s=>s.Side==LiveSoloSide.Left).TeamId;var right=x.Slots.Single(s=>s.Side==LiveSoloSide.Right).TeamId;
            var node=nodes?.Single(n=>n.MatchId==x.Id);return new LiveSoloCorrectionImpact(x.Id,x.ConcurrencyStamp,x.State,node?.WasStarted??RequiresReplay(x),
                left,left is Guid l?names.GetValueOrDefault(l):null,right,right is Guid r?names.GetValueOrDefault(r):null,node?.ReplacementMatchId); }).ToArray();
    }
    private async Task<LiveSoloCorrectionView> CorrectionViewAsync(LiveSoloResultCorrection correction,CancellationToken ct)
    {
        var ids=correction.Matches.Select(x=>x.MatchId).ToArray();
        var matches=await db.LiveSoloMatches.Include(x=>x.Slots).Where(x=>ids.Contains(x.Id)).OrderBy(x=>x.Lane).ThenBy(x=>x.Stage).ThenBy(x=>x.Position).ToArrayAsync(ct);
        var actors=await db.Users.Where(x=>x.Id==correction.ActorUserId || x.Id==correction.ResolvedByUserId).ToDictionaryAsync(x=>x.Id,x=>x.UserName,ct);
        return CorrectionView(correction,await ImpactsAsync(matches,correction.Matches,ct),actors);
    }
    private static LiveSoloCorrectionView CorrectionView(LiveSoloResultCorrection correction,IReadOnlyList<LiveSoloCorrectionImpact> impacts,IReadOnlyDictionary<Guid,string> actors) =>
        new(correction.Id,correction.MatchId,correction.ConcurrencyStamp,correction.State,correction.WinnerTeamId,correction.LeftWins,correction.RightWins,
            correction.Reason,correction.CreatedAt,correction.ResolutionReason,correction.ResolvedAt,impacts,
            correction.PreviousWinnerTeamId,correction.PreviousLeftWins,correction.PreviousRightWins,actors[correction.ActorUserId],
            correction.ResolvedByUserId is Guid resolved ? actors[resolved] : null);
}
