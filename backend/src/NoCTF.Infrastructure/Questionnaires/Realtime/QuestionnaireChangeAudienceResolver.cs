using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Questionnaires.Realtime;
using NoCTF.Domain.Identity;
using NoCTF.Infrastructure.Persistence;

namespace NoCTF.Infrastructure.Questionnaires.Realtime;

public sealed class QuestionnaireChangeAudienceResolver(IDbContextFactory<NoCtfDbContext> contexts)
{
    public async Task<HashSet<Guid>> ResolveAsync(QuestionnaireChanged change, CancellationToken ct)
    {
        await using var db = await contexts.CreateDbContextAsync(ct);
        var activity = await db.QuestionnaireActivities.AsNoTracking().SingleOrDefaultAsync(item => item.Id == change.ActivityId, ct);
        if (activity is null) return [];
        var users = new HashSet<Guid> { activity.OwnerId };
        var participants = db.QuestionnaireParticipants.AsNoTracking().Where(item => item.ActivityId == change.ActivityId
            && (change.ParticipantId == null || item.Id == change.ParticipantId));
        users.UnionWith(await participants.Select(item => item.UserId).ToArrayAsync(ct));
        return (await db.Users.Where(user => user.AccountStatus == UserAccountStatus.Active
            && (users.Contains(user.Id) || user.Role == UserRole.Administrator)).Select(user => user.Id).ToArrayAsync(ct)).ToHashSet();
    }
}
