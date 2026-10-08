using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Runtime.Access;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Identity;
using NoCTF.Domain.LiveSolo;
using NoCTF.Domain.Teams;
using NoCTF.Infrastructure.Persistence;

namespace NoCTF.Infrastructure.LiveSolo.Resources;

public sealed class LiveSoloExecutionAccess(NoCtfDbContext db) : IExecutionScopeAccess
{
    public async Task<bool> CanAccessAsync(ExecutionScopeAccessRequest request, CancellationToken ct)
    {
        if (request.ActorId == Guid.Empty || request.TeamId is not Guid teamId) return false;
        if (!await db.Set<LiveSoloCompetitionModeConfiguration>().AnyAsync(x => x.CompetitionId == request.CompetitionId && x.Enabled, ct)) return false;
        if (!await db.Users.AsNoTracking().AnyAsync(x => x.Id == request.ActorId && x.AccountStatus == UserAccountStatus.Active, ct)) return false;
        if (!await db.Teams.AsNoTracking().AnyAsync(x => x.Id == teamId && x.CompetitionId == request.CompetitionId
            && !x.IsBanned && x.RegistrationStatus == TeamRegistrationStatus.Approved && x.Members.Any(m => m.UserId == request.ActorId), ct)) return false;
        var scope = await db.LiveSoloRoundQuestions.AsNoTracking().Where(x => x.Id == request.ExecutionScopeId
                && x.CompetitionChallengeId == request.CompetitionChallengeId)
            .Join(db.LiveSoloRounds.AsNoTracking(), x => x.RoundId, x => x.Id, (question, round) => new { Question = question, Round = round })
            .Join(db.LiveSoloMatches.AsNoTracking(), x => x.Round.MatchId, x => x.Id, (x, match) => new { x.Question, x.Round, Match = match })
            .Join(db.Competitions.AsNoTracking(), x => x.Match.CompetitionId, x => x.Id, (x, competition) => new { x.Question, x.Round, x.Match, Competition = competition })
            .Where(x => x.Match.CompetitionId == request.CompetitionId && x.Competition.Mode == GameMode.LiveSolo
                && x.Match.CurrentRoundId == x.Round.Id && x.Match.Slots.Any(s => s.TeamId == teamId && s.RosterLockedAt != null)
                && x.Match.Roster.Any(r => r.UserId == request.ActorId && r.TeamId == teamId))
            .Select(x => new { x.Question.OpenedAt, RoundState = x.Round.State, MatchState = x.Match.State, CompetitionState = x.Competition.Status })
            .SingleOrDefaultAsync(ct);
        if (scope is null || scope.OpenedAt is null || scope.OpenedAt > request.Now) return false;
        if (scope.CompetitionState is not (CompetitionStatus.Running or CompetitionStatus.Paused)
            || scope.MatchState is not (LiveSoloMatchState.Running or LiveSoloMatchState.Paused)
            || scope.RoundState is not (LiveSoloRoundState.Running or LiveSoloRoundState.ConfirmingResult)) return false;
        return request.Operation is ExecutionScopeOperation.Read || scope.CompetitionState == CompetitionStatus.Running
            && scope.MatchState == LiveSoloMatchState.Running && scope.RoundState == LiveSoloRoundState.Running;
    }
}
