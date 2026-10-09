using Microsoft.EntityFrameworkCore;
using NoCTF.Application.LiveSolo.Resources;
using NoCTF.Application.Teams.Moderation;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Identity;
using NoCTF.Domain.LiveSolo;
using NoCTF.Domain.Teams;
using NoCTF.Infrastructure.Persistence;

namespace NoCTF.Infrastructure.LiveSolo.Resources;

public sealed class LiveSoloPostgameQuestionAccess(NoCtfDbContext db,ICompetitionModerationAuthorizer authorizer) : ILiveSoloPostgameQuestionAccess
{
    public async Task<IReadOnlyList<LiveSoloPostgameQuestion>?> ListAsync(Guid competitionId,Guid matchId,Guid actorId,CancellationToken ct)
    {
        if (!await db.Users.AnyAsync(x=>x.Id==actorId&&x.AccountStatus==UserAccountStatus.Active,ct)
            || !await db.Competitions.AnyAsync(x=>x.Id==competitionId&&x.Mode==GameMode.LiveSolo&&x.Status==CompetitionStatus.Finished,ct)
            || !await db.Set<LiveSoloCompetitionModeConfiguration>().AnyAsync(x=>x.CompetitionId==competitionId&&x.Enabled,ct)
            || !await db.LiveSoloMatches.AnyAsync(x=>x.Id==matchId&&x.CompetitionId==competitionId,ct)) return null;
        if (!await authorizer.CanObserveAsync(actorId,competitionId,ct)&&!await db.Teams.AnyAsync(t=>t.CompetitionId==competitionId&&!t.IsBanned
            &&t.RegistrationStatus==TeamRegistrationStatus.Approved&&t.Members.Any(m=>m.UserId==actorId),ct)) return null;
        return await db.LiveSoloRoundQuestions.AsNoTracking().Where(x=>x.OpenedAt!=null)
            .Join(db.LiveSoloRounds.AsNoTracking().Where(x=>x.MatchId==matchId),q=>q.RoundId,r=>r.Id,(q,r)=>new {Question=q,Round=r})
            .Join(db.CompetitionChallenges.AsNoTracking(),x=>x.Question.CompetitionChallengeId,c=>c.Id,(x,c)=>new {x.Question,x.Round,Challenge=c})
            .Join(db.Challenges.AsNoTracking(),x=>x.Challenge.ChallengeId,c=>c.Id,(x,c)=>new LiveSoloPostgameQuestion(x.Question.Id,x.Round.Id,
                x.Challenge.Id,x.Round.Number,x.Question.Position,x.Challenge.CustomTitle??c.Title,x.Question.OpenedAt!.Value))
            .OrderBy(x=>x.RoundNumber).ThenBy(x=>x.Position).ToArrayAsync(ct);
    }
    public async Task<Guid?> ResolveAsync(LiveSoloResourceRequest request,CancellationToken ct)
    {
        if (!await db.Users.AnyAsync(x=>x.Id==request.ActorId&&x.AccountStatus==UserAccountStatus.Active,ct)
            || !await db.Competitions.AnyAsync(x=>x.Id==request.CompetitionId&&x.Mode==GameMode.LiveSolo&&x.Status==CompetitionStatus.Finished,ct)
            || !await db.Set<LiveSoloCompetitionModeConfiguration>().AnyAsync(x=>x.CompetitionId==request.CompetitionId&&x.Enabled,ct)) return null;
        var question=await db.LiveSoloRoundQuestions.AsNoTracking().Where(x=>x.Id==request.QuestionId&&x.RoundId==request.RoundId&&x.OpenedAt!=null)
            .Join(db.LiveSoloRounds.AsNoTracking().Where(x=>x.MatchId==request.MatchId),q=>q.RoundId,r=>r.Id,(q,r)=>new {Question=q,Round=r})
            .Join(db.LiveSoloMatches.AsNoTracking().Where(x=>x.Id==request.MatchId&&x.CompetitionId==request.CompetitionId),x=>x.Round.MatchId,m=>m.Id,(x,m)=>x.Question)
            .Select(x=>(Guid?)x.CompetitionChallengeId).SingleOrDefaultAsync(ct);
        if (question is null) return null;
        if (await authorizer.CanObserveAsync(request.ActorId,request.CompetitionId,ct)) return question;
        return await db.Teams.AnyAsync(t=>t.CompetitionId==request.CompetitionId&&!t.IsBanned&&t.RegistrationStatus==TeamRegistrationStatus.Approved
            &&t.Members.Any(member=>member.UserId==request.ActorId),ct)?question:null;
    }
}
