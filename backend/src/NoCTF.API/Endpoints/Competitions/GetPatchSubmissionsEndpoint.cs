using System.Security.Claims;
using FastEndpoints;
using Microsoft.EntityFrameworkCore;
using NoCTF.Core;
using NoCTF.Infrastructure;

namespace NoCTF.API.Endpoints.Competitions;

public class GetPatchSubmissionsRequest
{
    public Guid Id { get; set; }
}

public class GetPatchSubmissionRequest
{
    public Guid Id { get; set; }
    public Guid SubmissionId { get; set; }
}

public class PatchSubmissionDto
{
    public Guid Id { get; set; }
    public Guid CompetitionId { get; set; }
    public Guid TeamId { get; set; }
    public Guid ChallengeId { get; set; }
    public AwdpPatchStatus Status { get; set; }
    public AwdpFixStatus FixStatus { get; set; }
    public int AttemptNumber { get; set; }
    public string FileName { get; set; } = string.Empty;
    public string FixEntry { get; set; } = string.Empty;
    public DateTime SubmittedAt { get; set; }
    public DateTime? ValidatedAt { get; set; }
    public string? ValidationDetail { get; set; }
}

public class GetPatchSubmissionsEndpoint(ApplicationDbContext db)
    : Endpoint<GetPatchSubmissionsRequest, List<PatchSubmissionDto>>
{
    public override void Configure()
    {
        Get("/api/competitions/{id}/patch-submissions");
        Claims(ClaimTypes.NameIdentifier);
    }

    public override async Task HandleAsync(GetPatchSubmissionsRequest req, CancellationToken ct)
    {
        var teamId = await GetUserTeamIdAsync(db, User, req.Id, ct);
        if (teamId is null)
        {
            await SendForbiddenAsync(ct);
            return;
        }

        var submissions = await db.AwdpPatchSubmissions
            .IgnoreQueryFilters()
            .Where(s => s.CompetitionId == req.Id && s.TeamId == teamId.Value)
            .OrderByDescending(s => s.SubmittedAt)
            .Select(s => new PatchSubmissionDto
            {
                Id = s.Id,
                CompetitionId = s.CompetitionId,
                TeamId = s.TeamId,
                ChallengeId = s.ChallengeId,
                Status = s.Status,
                FixStatus = AwdpPlayerDefenseResult.ToVisibleFixStatus(s.FixStatus),
                AttemptNumber = s.AttemptNumber,
                FileName = s.FileName,
                FixEntry = s.FixEntry,
                SubmittedAt = s.SubmittedAt,
                ValidatedAt = s.ValidatedAt,
                ValidationDetail = AwdpPlayerDefenseResult.ToVisibleDetail(s.FixStatus)
            })
            .ToListAsync(ct);

        await SendAsync(submissions, cancellation: ct);
    }

    internal static async Task<Guid?> GetUserTeamIdAsync(
        ApplicationDbContext db,
        ClaimsPrincipal user,
        Guid competitionId,
        CancellationToken ct)
    {
        var userIdClaim = user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (!Guid.TryParse(userIdClaim, out var userId))
            return null;

        return await db.TeamMembers
            .AsNoTracking()
            .Join(db.Teams.IgnoreQueryFilters().Where(t => t.CompetitionId == competitionId),
                tm => tm.TeamId,
                t => t.Id,
                (tm, t) => new { tm.UserId, TeamId = t.Id })
            .Where(x => x.UserId == userId)
            .Select(x => (Guid?)x.TeamId)
            .FirstOrDefaultAsync(ct);
    }
}

public class GetPatchSubmissionEndpoint(ApplicationDbContext db)
    : Endpoint<GetPatchSubmissionRequest, PatchSubmissionDto>
{
    public override void Configure()
    {
        Get("/api/competitions/{id}/patch-submissions/{submissionId}");
        Claims(ClaimTypes.NameIdentifier);
    }

    public override async Task HandleAsync(GetPatchSubmissionRequest req, CancellationToken ct)
    {
        var teamId = await GetPatchSubmissionsEndpoint.GetUserTeamIdAsync(db, User, req.Id, ct);
        if (teamId is null)
        {
            await SendForbiddenAsync(ct);
            return;
        }

        var submission = await db.AwdpPatchSubmissions
            .IgnoreQueryFilters()
            .Where(s => s.Id == req.SubmissionId && s.CompetitionId == req.Id && s.TeamId == teamId.Value)
            .Select(s => new PatchSubmissionDto
            {
                Id = s.Id,
                CompetitionId = s.CompetitionId,
                TeamId = s.TeamId,
                ChallengeId = s.ChallengeId,
                Status = s.Status,
                FixStatus = AwdpPlayerDefenseResult.ToVisibleFixStatus(s.FixStatus),
                AttemptNumber = s.AttemptNumber,
                FileName = s.FileName,
                FixEntry = s.FixEntry,
                SubmittedAt = s.SubmittedAt,
                ValidatedAt = s.ValidatedAt,
                ValidationDetail = AwdpPlayerDefenseResult.ToVisibleDetail(s.FixStatus)
            })
            .FirstOrDefaultAsync(ct);

        if (submission is null)
        {
            await SendNotFoundAsync(ct);
            return;
        }

        await SendAsync(submission, cancellation: ct);
    }
}
