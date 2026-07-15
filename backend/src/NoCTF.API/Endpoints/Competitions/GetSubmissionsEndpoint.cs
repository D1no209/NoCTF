using System.Security.Claims;
using FastEndpoints;
using Microsoft.EntityFrameworkCore;
using NoCTF.Core;
using NoCTF.Infrastructure;

namespace NoCTF.API.Endpoints.Competitions;

public class GetSubmissionsRequest
{
    public Guid Id { get; set; }
}

public class SolvedChallengeDto
{
    public Guid ChallengeId { get; set; }
    public DateTime SolvedAt { get; set; }
    public bool IsFullySolved { get; set; } = true;
}

public class SolvedFlagDto
{
    public Guid ChallengeId { get; set; }
    public Guid FlagId { get; set; }
    public int Stage { get; set; }
    public int Score { get; set; }
    public DateTime SolvedAt { get; set; }
}

public class GetSubmissionsResponse
{
    public Guid CompetitionId { get; set; }
    public Guid TeamId { get; set; }
    public List<SolvedChallengeDto> SolvedChallenges { get; set; } = [];
    public List<SolvedFlagDto> SolvedFlags { get; set; } = [];
}

/// <summary>
/// GET /api/competitions/{id}/submissions — returns the authenticated user's team correct submissions.
/// </summary>
public class GetSubmissionsEndpoint(ApplicationDbContext dbContext) : Endpoint<GetSubmissionsRequest, GetSubmissionsResponse>
{
    public override void Configure()
    {
        Get("/api/competitions/{id}/submissions");
        Claims(ClaimTypes.NameIdentifier);
    }

    public override async Task HandleAsync(GetSubmissionsRequest req, CancellationToken ct)
    {
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (!Guid.TryParse(userIdClaim, out var userId))
        {
            await SendUnauthorizedAsync(ct);
            return;
        }

        var competition = await dbContext.Competitions
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(c => c.Id == req.Id)
            .Select(c => new { c.Status, c.StartTime })
            .FirstOrDefaultAsync(ct);

        if (competition is null || !PublicCompetitionGuard.IsPublic(competition.Status))
        {
            await SendNotFoundAsync(ct);
            return;
        }

        if (DateTime.UtcNow < competition.StartTime && competition.Status != CompetitionStatus.Finished)
        {
            await SendAsync(new GetSubmissionsResponse
            {
                CompetitionId = req.Id,
                TeamId = Guid.Empty,
                SolvedChallenges = [],
                SolvedFlags = []
            }, cancellation: ct);
            return;
        }

        // Find the user's team in this competition
        var teamMember = await dbContext.TeamMembers
            .AsNoTracking()
            .Join(dbContext.Teams.IgnoreQueryFilters().Where(t => t.CompetitionId == req.Id),
                  tm => tm.TeamId,
                  t => t.Id,
                  (tm, t) => new { tm.UserId, t.Id, t.CompetitionId, t.RegistrationStatus, t.IsBanned })
            .FirstOrDefaultAsync(x => x.UserId == userId, ct);

        if (teamMember is null)
        {
            // User has no team in this competition — return empty
            await SendAsync(new GetSubmissionsResponse
            {
                CompetitionId = req.Id,
                TeamId = Guid.Empty,
                SolvedChallenges = []
            }, cancellation: ct);
            return;
        }

        if (teamMember.RegistrationStatus != TeamRegistrationStatus.Approved)
        {
            await SendStringAsync("team_not_approved", 403, cancellation: ct);
            return;
        }

        if (teamMember.IsBanned)
        {
            await SendStringAsync("team_banned", 403, cancellation: ct);
            return;
        }

        var activeChallengeIds = await dbContext.Challenges
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(challenge =>
                challenge.CompetitionId == req.Id &&
                !challenge.IsDeleting)
            .Select(challenge => challenge.Id)
            .ToListAsync(ct);

        var correctSubmissions = await dbContext.Submissions
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(s => s.CompetitionId == req.Id
                     && s.TeamId == teamMember.Id
                     && s.IsCorrect
                     && activeChallengeIds.Contains(s.ChallengeId))
            .Select(s => new { s.ChallengeId, s.PenetrationFlagId, s.SubmittedAt })
            .ToListAsync(ct);

        var solvedFlags = await BuildVisibleSolvedFlagsQuery(
                dbContext,
                req.Id,
                teamMember.Id,
                activeChallengeIds)
            .ToListAsync(ct);

        var penetrationChallengeIds = await dbContext.Challenges
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(c =>
                c.CompetitionId == req.Id &&
                activeChallengeIds.Contains(c.Id) &&
                c.TypeId.ToLower() == "penetration" &&
                !c.IsDeleting)
            .Select(c => c.Id)
            .ToListAsync(ct);
        var visibleFlagCounts = await dbContext.PenetrationFlags
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(f =>
                f.CompetitionId == req.Id &&
                f.Visible &&
                activeChallengeIds.Contains(f.ChallengeId))
            .GroupBy(f => f.ChallengeId)
            .Select(g => new { ChallengeId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.ChallengeId, x => x.Count, ct);

        var solvedChallenges = correctSubmissions
            .Where(s => !s.PenetrationFlagId.HasValue)
            .GroupBy(s => s.ChallengeId)
            .Select(g => new SolvedChallengeDto
            {
                ChallengeId = g.Key,
                SolvedAt = g.Min(s => s.SubmittedAt)
            })
            .ToList();

        var solvedFlagsByChallenge = solvedFlags.ToLookup(flag => flag.ChallengeId);
        foreach (var challengeId in penetrationChallengeIds)
        {
            var solvedForChallenge = solvedFlagsByChallenge[challengeId].ToList();
            var requiredCount = visibleFlagCounts.GetValueOrDefault(challengeId);
            if (requiredCount > 0 && solvedForChallenge.Count >= requiredCount)
            {
                solvedChallenges.Add(new SolvedChallengeDto
                {
                    ChallengeId = challengeId,
                    SolvedAt = solvedForChallenge.Max(f => f.SolvedAt),
                    IsFullySolved = true
                });
            }
        }

        await SendAsync(new GetSubmissionsResponse
        {
            CompetitionId = req.Id,
            TeamId = teamMember.Id,
            SolvedChallenges = solvedChallenges,
            SolvedFlags = solvedFlags
        }, cancellation: ct);
    }

    internal static IQueryable<SolvedFlagDto> BuildVisibleSolvedFlagsQuery(
        ApplicationDbContext dbContext,
        Guid competitionId,
        Guid teamId,
        IReadOnlyCollection<Guid> activeChallengeIds)
        => dbContext.PenetrationFlags
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(flag =>
                flag.CompetitionId == competitionId &&
                flag.Visible &&
                activeChallengeIds.Contains(flag.ChallengeId))
            .Join(
                dbContext.Submissions.IgnoreQueryFilters().AsNoTracking().Where(submission =>
                    submission.CompetitionId == competitionId &&
                    submission.TeamId == teamId &&
                    submission.IsCorrect &&
                    submission.PenetrationFlagId != null),
                flag => flag.Id,
                submission => submission.PenetrationFlagId!.Value,
                (flag, submission) => new SolvedFlagDto
                {
                    ChallengeId = flag.ChallengeId,
                    FlagId = flag.Id,
                    Stage = flag.Stage,
                    Score = flag.Score,
                    SolvedAt = submission.SubmittedAt
                });
}
