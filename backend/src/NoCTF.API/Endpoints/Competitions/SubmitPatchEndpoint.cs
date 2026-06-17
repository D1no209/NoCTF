using System.Security.Claims;
using FastEndpoints;
using Microsoft.EntityFrameworkCore;
using NoCTF.Infrastructure;
using NoCTF.PluginBase;

namespace NoCTF.API.Endpoints.Competitions;

public class SubmitPatchRequest
{
    public Guid Id { get; set; }
    public Guid ChallengeId { get; set; }
}

public class SubmitPatchResponse
{
    public Guid SubmissionId { get; set; }
    public string Status { get; set; } = string.Empty;
}

/// <summary>
/// POST /api/competitions/{id}/challenges/{challengeId}/patch
/// Accepts multipart/form-data with a fix.tar.gz file.
/// Creates a Pending patch submission and triggers async validation.
/// </summary>
public class SubmitPatchEndpoint(ApplicationDbContext dbContext, IAwdpPatchService patchService)
    : Endpoint<SubmitPatchRequest, SubmitPatchResponse>
{
    public override void Configure()
    {
        Post("/api/competitions/{id}/challenges/{challengeId}/patch");
        Claims(ClaimTypes.NameIdentifier);
        AllowFileUploads();
    }

    public override async Task HandleAsync(SubmitPatchRequest req, CancellationToken ct)
    {
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (!Guid.TryParse(userIdClaim, out var userId))
        {
            await SendUnauthorizedAsync(ct);
            return;
        }

        // Find the user's team in this competition
        var teamMember = await dbContext.TeamMembers
            .AsNoTracking()
            .Join(dbContext.Teams.IgnoreQueryFilters().Where(t => t.CompetitionId == req.Id),
                  tm => tm.TeamId,
                  t => t.Id,
                  (tm, t) => new { tm.UserId, TeamId = t.Id })
            .FirstOrDefaultAsync(x => x.UserId == userId, ct);

        if (teamMember is null)
        {
            await SendAsync(new SubmitPatchResponse { Status = "no_team" }, 400, ct);
            return;
        }

        // Expect a single file upload named "file" or the first file
        var file = Files.FirstOrDefault();
        if (file is null)
        {
            await SendAsync(new SubmitPatchResponse { Status = "no_file" }, 400, ct);
            return;
        }

        await using var stream = file.OpenReadStream();
        var submissionId = await patchService.SubmitPatchAsync(
            req.Id,
            teamMember.TeamId,
            req.ChallengeId,
            stream,
            file.FileName,
            ct);

        // Fire-and-forget validation (background task)
        _ = Task.Run(async () =>
        {
            try
            {
                await patchService.ValidatePatchAsync(submissionId);
            }
            catch
            {
                // Validation errors are persisted inside ValidatePatchAsync
            }
        }, CancellationToken.None);

        await SendAsync(new SubmitPatchResponse
        {
            SubmissionId = submissionId,
            Status = "pending"
        }, 202, ct);
    }
}
