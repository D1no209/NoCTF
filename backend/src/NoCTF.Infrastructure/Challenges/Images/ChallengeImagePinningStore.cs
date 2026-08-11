using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Challenges.Images;
using NoCTF.Domain.Challenges;
using NoCTF.Domain.Competitions;
using NoCTF.Infrastructure.Persistence;

namespace NoCTF.Infrastructure.Challenges.Images;

public sealed class ChallengeImagePinningStore(NoCtfDbContext db)
    : IChallengeImagePinningStore
{
    public Task<ChallengeImagePinSnapshot?> LoadChallengeAsync(
        Guid challengeId,
        CancellationToken ct) =>
        Project(db.Challenges.AsNoTracking().Where(challenge => challenge.Id == challengeId))
            .SingleOrDefaultAsync(ct);

    public Task<ChallengeImagePinSnapshot?> LoadCompetitionChallengeAsync(
        Guid competitionId,
        Guid competitionChallengeId,
        CancellationToken ct) =>
        Project(db.CompetitionChallenges.AsNoTracking()
            .Where(item =>
                item.CompetitionId == competitionId
                && item.Id == competitionChallengeId
                && item.DeletedAt == null)
            .Join(
                db.Challenges.AsNoTracking(),
                item => item.ChallengeId,
                challenge => challenge.Id,
                (_, challenge) => challenge))
            .SingleOrDefaultAsync(ct);

    public async Task<CompetitionImagePinSnapshot> LoadCompetitionAsync(
        Guid competitionId,
        bool includeDeleted,
        CancellationToken ct)
    {
        var exists = await db.Competitions.IgnoreQueryFilters().AsNoTracking().AnyAsync(
            competition => competition.Id == competitionId
                && (includeDeleted || competition.DeletedAt == null),
            ct);
        if (!exists)
            return new(false, []);
        var challenges = await Project(db.CompetitionChallenges.AsNoTracking()
                .Where(item =>
                    item.CompetitionId == competitionId
                    && item.IsPublished
                    && item.DeletedAt == null)
                .Join(
                    db.Challenges.AsNoTracking(),
                    item => item.ChallengeId,
                    challenge => challenge.Id,
                    (_, challenge) => challenge)
                .Distinct())
            .ToArrayAsync(ct);
        return new(true, challenges);
    }

    public async Task<ChallengeImagePinCommitState> ApplyAsync(
        IReadOnlyList<ChallengeImagePinPlan> plans,
        DateTimeOffset updatedAt,
        CancellationToken ct)
    {
        if (plans.Count == 0)
            return ChallengeImagePinCommitState.Succeeded;
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        foreach (var plan in plans.OrderBy(item => item.Source.ChallengeId))
        {
            var changed = await db.Challenges
                .Where(challenge =>
                    challenge.Id == plan.Source.ChallengeId
                    && challenge.Revision == plan.Source.Revision
                    && challenge.DeletedAt == null)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(challenge => challenge.DefinitionJson, plan.PinnedDefinitionJson)
                    .SetProperty(challenge => challenge.Revision, challenge => challenge.Revision + 1)
                    .SetProperty(challenge => challenge.UpdatedAt, updatedAt), ct);
            if (changed == 1)
                continue;
            await transaction.RollbackAsync(ct);
            var exists = await db.Challenges.IgnoreQueryFilters().AsNoTracking().AnyAsync(
                challenge => challenge.Id == plan.Source.ChallengeId
                    && challenge.DeletedAt == null,
                ct);
            return exists
                ? ChallengeImagePinCommitState.RevisionConflict
                : ChallengeImagePinCommitState.ChallengeNotFound;
        }
        await transaction.CommitAsync(ct);
        return ChallengeImagePinCommitState.Succeeded;
    }

    public Task<bool> RequiresPinnedDefinitionAsync(
        Guid challengeId,
        CancellationToken ct) =>
        RequiresPinnedDefinitionQuery(challengeId).AnyAsync(ct);

    public async Task<ChallengeTemplateWriteBoundary> LockTemplateWriteBoundaryAsync(
        Guid challengeId,
        CancellationToken ct)
    {
        EnsureRelational();
        _ = await db.Competitions
            .FromSqlInterpolated($$"""
                SELECT competition.*
                FROM competitions AS competition
                WHERE competition.deleted_at IS NULL
                  AND EXISTS (
                    SELECT 1
                    FROM competition_challenges AS competition_challenge
                    WHERE competition_challenge.competition_id = competition.id
                      AND competition_challenge.challenge_id = {{challengeId}}
                      AND competition_challenge.deleted_at IS NULL)
                ORDER BY competition.id
                FOR UPDATE
                """)
            .IgnoreQueryFilters()
            .AsNoTracking()
            .ToArrayAsync(ct);
        var challengeExists = await db.Challenges
            .FromSqlInterpolated($$"""
                SELECT challenge.*
                FROM challenges AS challenge
                WHERE challenge.id = {{challengeId}}
                FOR UPDATE
                """)
            .IgnoreQueryFilters()
            .AsNoTracking()
            .AnyAsync(ct);
        if (!challengeExists)
            return new(false, false);

        return new(
            true,
            await RequiresPinnedDefinitionQuery(challengeId).AnyAsync(ct));
    }

    public async Task<CompetitionImagePublicationBoundary> LockCompetitionPublicationBoundaryAsync(
        Guid competitionId,
        bool includeDeleted,
        CancellationToken ct)
    {
        EnsureRelational();
        var competitions = await db.Competitions
            .FromSqlInterpolated($$"""
                SELECT competition.*
                FROM competitions AS competition
                WHERE competition.id = {{competitionId}}
                  AND ({{includeDeleted}} OR competition.deleted_at IS NULL)
                FOR UPDATE
                """)
            .IgnoreQueryFilters()
            .AsNoTracking()
            .ToArrayAsync(ct);
        var competition = competitions.SingleOrDefault();
        if (competition is null)
            return new(false, null, []);
        var challenges = competition.Status is CompetitionStatus.Published
            or CompetitionStatus.Running
            or CompetitionStatus.Paused
            ? await LockCompetitionAsync(competitionId, ct)
            : [];
        return new(true, competition.Status, challenges);
    }

    private IQueryable<Competition> RequiresPinnedDefinitionQuery(Guid challengeId) =>
        db.CompetitionChallenges.AsNoTracking()
            .Where(item =>
                item.ChallengeId == challengeId
                && item.IsPublished
                && item.DeletedAt == null)
            .Join(
                db.Competitions.AsNoTracking(),
                item => item.CompetitionId,
                competition => competition.Id,
                (_, competition) => competition)
            .Where(competition =>
                competition.DeletedAt == null
                && (competition.Status == CompetitionStatus.Published
                    || competition.Status == CompetitionStatus.Running
                    || competition.Status == CompetitionStatus.Paused));

    public async Task<IReadOnlyList<ChallengeImagePinSnapshot>> LockCompetitionAsync(
        Guid competitionId,
        CancellationToken ct)
    {
        EnsureRelational();
        var challenges = await db.Challenges
            .FromSqlInterpolated($$"""
                SELECT challenge.*
                FROM challenges AS challenge
                WHERE challenge.deleted_at IS NULL
                  AND EXISTS (
                    SELECT 1
                    FROM competition_challenges AS competition_challenge
                    WHERE competition_challenge.challenge_id = challenge.id
                      AND competition_challenge.competition_id = {{competitionId}}
                      AND competition_challenge.is_published
                      AND competition_challenge.deleted_at IS NULL)
                ORDER BY challenge.id
                FOR UPDATE
                """)
            .IgnoreQueryFilters()
            .AsNoTracking()
            .ToArrayAsync(ct);
        return challenges.Select(Map).ToArray();
    }

    public async Task<ChallengeImagePinSnapshot?> LockCompetitionChallengeAsync(
        Guid competitionId,
        Guid competitionChallengeId,
        CancellationToken ct)
    {
        EnsureRelational();
        var challenges = await db.Challenges
            .FromSqlInterpolated($$"""
                SELECT challenge.*
                FROM challenges AS challenge
                WHERE challenge.deleted_at IS NULL
                  AND EXISTS (
                    SELECT 1
                    FROM competition_challenges AS competition_challenge
                    WHERE competition_challenge.challenge_id = challenge.id
                      AND competition_challenge.competition_id = {{competitionId}}
                      AND competition_challenge.id = {{competitionChallengeId}})
                FOR UPDATE
                """)
            .IgnoreQueryFilters()
            .AsNoTracking()
            .ToArrayAsync(ct);
        return challenges.Select(Map).SingleOrDefault();
    }

    private void EnsureRelational()
    {
        if (!db.Database.IsRelational())
            throw new InvalidOperationException("Challenge image publication locks require a relational database.");
    }

    private static IQueryable<ChallengeImagePinSnapshot> Project(
        IQueryable<Challenge> challenges) =>
        challenges.Select(challenge => new ChallengeImagePinSnapshot(
            challenge.Id,
            challenge.Mode,
            challenge.DefinitionJson,
            challenge.Revision));

    private static ChallengeImagePinSnapshot Map(Challenge challenge) =>
        new(challenge.Id, challenge.Mode, challenge.DefinitionJson, challenge.Revision);
}
