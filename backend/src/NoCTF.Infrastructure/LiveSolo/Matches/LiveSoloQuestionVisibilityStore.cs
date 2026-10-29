using Microsoft.EntityFrameworkCore;
using NoCTF.Domain.LiveSolo;
using NoCTF.Application.LiveSolo.Realtime;
using NoCTF.Application.LiveSolo.Rounds;

namespace NoCTF.Infrastructure.LiveSolo.Matches;

public sealed partial class LiveSoloMatchStore
{
    public async Task<LiveSoloFailure?> SetQuestionVisibilityAsync(Guid competitionId, Guid matchId, Guid actorId,
        bool teammatesMayViewQuestions, CancellationToken ct)
    {
        if (!await ActiveAsync(actorId, ct) || !await authorizer.CanJudgeAsync(actorId, competitionId, ct)) return LiveSoloFailure.Forbidden;
        var match = await db.LiveSoloMatches.SingleOrDefaultAsync(x => x.Id == matchId && x.CompetitionId == competitionId, ct);
        if (match is null) return LiveSoloFailure.NotFound;
        match.TeammatesMayViewQuestions = teammatesMayViewQuestions;
        await db.SaveChangesAsync(ct);
        await messages.PublishAsync(new LiveSoloMatchChanged(competitionId, matchId, (clock ?? TimeProvider.System).GetUtcNow()));
        await messages.FlushCommittedMessagesAsync();
        return null;
    }
}
