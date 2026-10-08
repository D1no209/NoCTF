using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Challenges.WriteUps;
using NoCTF.Domain.Challenges.WriteUps;

namespace NoCTF.Infrastructure.Challenges.WriteUps;

public sealed partial class ChallengeWriteUpStore
{
    public async Task<WriteUpReviewPage?> ListReviewAsync(WriteUpReviewQuery request, CancellationToken ct)
    {
        if (!await authorizer.CanObserveAsync(request.ActorId, request.CompetitionId, ct)) return null;
        var query = db.ChallengeWriteUps.AsNoTracking().Where(x => x.CompetitionId == request.CompetitionId);
        if (request.ChallengeId is Guid challengeId) query = query.Where(x => x.CompetitionChallengeId == challengeId);
        if (request.Source is { } source) query = query.Where(x => x.Source == source);
        query = request.Filter switch
        {
            WriteUpReviewFilter.Submitted => query.Where(x => x.Versions.Any(v => v.Id == x.SubmittedVersionId && v.State == WriteUpVersionState.Submitted)),
            WriteUpReviewFilter.Published => query.Where(x => x.PublishedVersionId != null),
            WriteUpReviewFilter.Rejected => query.Where(x => x.Versions.Any(v => v.Id == x.SubmittedVersionId && v.State == WriteUpVersionState.Rejected)),
            WriteUpReviewFilter.Draft => query.Where(x => x.DraftVersionId != null),
            _ => query
        };
        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var text = request.Search.Trim().ToUpperInvariant();
            query = query.Where(root => db.Teams.Any(team => team.Id == root.TeamId && team.NormalizedName.Contains(text))
                || db.CompetitionChallenges.Any(challenge => challenge.Id == root.CompetitionChallengeId
                    && (challenge.NormalizedCustomTitle != null && challenge.NormalizedCustomTitle.Contains(text)
                        || db.Challenges.Any(template => template.Id == challenge.ChallengeId && template.NormalizedTitle.Contains(text)))));
        }
        var count = await query.CountAsync(ct);
        var roots = await query.OrderByDescending(x => x.UpdatedAt).ThenBy(x => x.Id)
            .Skip(Math.Max(0, request.Offset)).Take(Math.Clamp(request.Limit, 1, 100)).ToArrayAsync(ct);
        await LoadVersionMetadataAsync(roots, ct);
        var ids = roots.Select(x => x.CompetitionChallengeId).Distinct().ToArray();
        var titles = await db.CompetitionChallenges.AsNoTracking().IgnoreAutoIncludes().Where(x => ids.Contains(x.Id))
            .Join(db.Challenges.AsNoTracking(), x => x.ChallengeId, x => x.Id,
                (challenge, template) => new { challenge.Id, Title = challenge.CustomTitle ?? template.Title }).ToDictionaryAsync(x => x.Id, x => x.Title, ct);
        var teamIds = roots.Where(x => x.TeamId != null).Select(x => x.TeamId!.Value).ToArray();
        var names = await db.Teams.IgnoreQueryFilters().AsNoTracking().Where(x => teamIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, x => x.Name, ct);
        var actors = await ActorNamesAsync(roots, ct);
        var viewed = await db.WriteUpUnlockReceipts.AsNoTracking().Where(x => x.CompetitionId == request.CompetitionId
            && ids.Contains(x.CompetitionChallengeId)).GroupBy(x => x.CompetitionChallengeId)
            .Select(x => new { Id = x.Key, Count = x.Count() }).ToDictionaryAsync(x => x.Id, x => x.Count, ct);
        return new(roots.Select(x => Map(x, titles.GetValueOrDefault(x.CompetitionChallengeId, string.Empty),
                x.TeamId is Guid team ? names.GetValueOrDefault(team, string.Empty) : string.Empty, true, actors)
                with { ViewedTeamCount = viewed.GetValueOrDefault(x.CompetitionChallengeId) }).ToArray(), count,
            await authorizer.CanModerateAsync(request.ActorId, request.CompetitionId, ct), await authorizer.CanJudgeAsync(request.ActorId, request.CompetitionId, ct));
    }

    private async Task LoadVersionMetadataAsync(IReadOnlyList<ChallengeWriteUp> roots, CancellationToken ct,
        IReadOnlyList<Guid>? privateRoots = null)
    {
        var ids = roots.Select(x => x.Id).ToArray();
        var publishedIds = roots.Where(x => x.PublishedVersionId != null).Select(x => x.PublishedVersionId!.Value).ToArray();
        var query = db.ChallengeWriteUpVersions.AsNoTracking().Where(x => ids.Contains(x.WriteUpId));
        if (privateRoots is not null) query = query.Where(x => privateRoots.Contains(x.WriteUpId) || publishedIds.Contains(x.Id));
        var versions = await query.Select(x =>
            new ChallengeWriteUpVersion { Id = x.Id, WriteUpId = x.WriteUpId, Number = x.Number, Format = x.Format,
                State = x.State, ConcurrencyStamp = x.ConcurrencyStamp, ActorUserId = x.ActorUserId, UpdatedAt = x.UpdatedAt,
                SubmittedAt = x.SubmittedAt, ReviewedAt = x.ReviewedAt, ReviewReason = x.ReviewReason }).ToArrayAsync(ct);
        var byRoot = versions.ToLookup(x => x.WriteUpId);
        foreach (var root in roots) root.Versions = byRoot[root.Id].ToList();
    }
    private Task<Dictionary<Guid, string>> ActorNamesAsync(IReadOnlyList<ChallengeWriteUp> roots, CancellationToken ct)
    {
        var actors = roots.SelectMany(x => x.Versions).Select(x => x.ActorUserId).Distinct().ToArray();
        return db.Users.IgnoreQueryFilters().AsNoTracking().Where(x => actors.Contains(x.Id)).ToDictionaryAsync(x => x.Id, x => x.UserName, ct);
    }
    public async Task<IReadOnlyList<ChallengeWriteUpView>?> ListMineAsync(Guid competitionId, Guid actorId, DateTimeOffset now, CancellationToken ct)
    {
        var access = await readAccess.ResolveAsync(actorId, competitionId, now, ct);
        if (access?.TeamId is not Guid teamId || !await db.Competitions.AnyAsync(x => x.Id == competitionId && x.SingleWriteUpsEnabled, ct)) return null;
        var roots = await db.ChallengeWriteUps.AsNoTracking().Where(x => x.CompetitionId == competitionId && x.TeamId == teamId
            && db.CompetitionChallenges.Any(c => c.Id == x.CompetitionChallengeId && c.IsPublished)).OrderByDescending(x => x.UpdatedAt).ToArrayAsync(ct);
        await LoadVersionMetadataAsync(roots, ct);
        var ids = roots.Select(x => x.CompetitionChallengeId).ToArray();
        var titles = await db.CompetitionChallenges.AsNoTracking().IgnoreAutoIncludes().Where(x => ids.Contains(x.Id))
            .Join(db.Challenges.AsNoTracking(), x => x.ChallengeId, x => x.Id, (challenge, template) => new { challenge.Id, Title = challenge.CustomTitle ?? template.Title })
            .ToDictionaryAsync(x => x.Id, x => x.Title, ct);
        var name = await db.Teams.AsNoTracking().Where(x => x.Id == teamId).Select(x => x.Name).SingleAsync(ct);
        var actors = await ActorNamesAsync(roots, ct);
        return roots.Select(x => Map(x, titles.GetValueOrDefault(x.CompetitionChallengeId, string.Empty), name, true, actors)).ToArray();
    }
}
