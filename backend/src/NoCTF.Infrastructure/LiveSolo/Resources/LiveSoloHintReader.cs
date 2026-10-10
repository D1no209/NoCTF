using Microsoft.EntityFrameworkCore;
using NoCTF.Application.LiveSolo.Resources;
using NoCTF.Application.Runtime.Access;
using NoCTF.Infrastructure.Persistence;

namespace NoCTF.Infrastructure.LiveSolo.Resources;

public sealed class LiveSoloHintReader(NoCtfDbContext db, IExecutionScopeAccess access, TimeProvider clock) : ILiveSoloHintReader
{
    public async Task<IReadOnlyList<LiveSoloHintView>?> ReadAsync(LiveSoloResourceRequest request, CancellationToken ct)
    {
        var teamId = await db.Teams.AsNoTracking().Where(x => x.CompetitionId == request.CompetitionId && x.Members.Any(m => m.UserId == request.ActorId))
            .Select(x => (Guid?)x.Id).SingleOrDefaultAsync(ct);
        var question = await db.LiveSoloRoundQuestions.AsNoTracking().Where(x => x.Id == request.QuestionId && x.RoundId == request.RoundId)
            .Join(db.LiveSoloRounds.AsNoTracking().Where(x => x.MatchId == request.MatchId), x => x.RoundId, x => x.Id, (q, _) => q).SingleOrDefaultAsync(ct);
        if (teamId is not Guid team || question is null) return null;
        ExecutionScopeAccessRequest Scope() => new(request.QuestionId, request.CompetitionId, question.CompetitionChallengeId, team, request.ActorId, ExecutionScopeOperation.Read, clock.GetUtcNow());
        if (!await access.CanAccessAsync(Scope(), ct)) return null;
        var now = clock.GetUtcNow();
        var hints = await db.CompetitionChallenges.AsNoTracking().Where(x => x.Id == question.CompetitionChallengeId)
            .SelectMany(x => x.Hints).Where(x => x.PublishedAt != null && x.PublishedAt <= now && x.HiddenAt == null)
            .OrderBy(x => x.PublishedAt).ThenBy(x => x.Id).Select(x => new LiveSoloHintView(x.Id, x.Content, x.PublishedAt!.Value)).ToArrayAsync(ct);
        return await access.CanAccessAsync(Scope(), ct) ? hints : null;
    }
}
