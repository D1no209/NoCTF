using System.Security.Claims;
using FastEndpoints;
using Microsoft.EntityFrameworkCore;
using NoCTF.Application.BackgroundTasks;
using NoCTF.Application.Security;
using NoCTF.Core;
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
public class SubmitPatchEndpoint(
    ApplicationDbContext dbContext,
    IAwdpPatchService patchService,
    IBackgroundTaskQueue backgroundTaskQueue,
    IPatchArchiveValidator patchArchiveValidator,
    IConfiguration configuration)
    : Endpoint<SubmitPatchRequest, SubmitPatchResponse>
{
    private static readonly HashSet<string> AllowedContentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "application/gzip",
        "application/x-gzip",
        "application/octet-stream"
    };

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
                  (tm, t) => new { tm.UserId, TeamId = t.Id, t.RegistrationStatus, t.IsBanned })
            .FirstOrDefaultAsync(x => x.UserId == userId, ct);

        if (teamMember is null)
        {
            await SendAsync(new SubmitPatchResponse { Status = "no_team" }, 400, ct);
            return;
        }

        if (teamMember.RegistrationStatus != TeamRegistrationStatus.Approved)
        {
            await SendAsync(new SubmitPatchResponse { Status = "team_not_approved" }, 403, ct);
            return;
        }

        if (teamMember.IsBanned)
        {
            await SendAsync(new SubmitPatchResponse { Status = "team_banned" }, 403, ct);
            return;
        }

        // Expect a single file upload named "file" or the first file
        var file = Files.FirstOrDefault();
        if (file is null)
        {
            await SendAsync(new SubmitPatchResponse { Status = "no_file" }, 400, ct);
            return;
        }

        var maxBytes = configuration.GetValue<long>("PatchUpload:MaxBytes", 5 * 1024 * 1024);
        if (file.Length <= 0 || file.Length > maxBytes)
        {
            await SendAsync(new SubmitPatchResponse { Status = "invalid_size" }, 400, ct);
            return;
        }

        if (!file.FileName.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase) &&
            !file.FileName.EndsWith(".tgz", StringComparison.OrdinalIgnoreCase))
        {
            await SendAsync(new SubmitPatchResponse { Status = "invalid_extension" }, 400, ct);
            return;
        }

        if (!AllowedContentTypes.Contains(file.ContentType))
        {
            await SendAsync(new SubmitPatchResponse { Status = "invalid_content_type" }, 400, ct);
            return;
        }

        await using var stream = new MemoryStream();
        await file.CopyToAsync(stream, ct);
        stream.Position = 0;
        var validation = await patchArchiveValidator.ValidateAsync(stream, ct);
        if (!validation.IsValid)
        {
            await SendAsync(new SubmitPatchResponse { Status = validation.Error ?? "invalid_archive" }, 400, ct);
            return;
        }

        stream.Position = 0;
        var submissionId = await patchService.SubmitPatchAsync(
            req.Id,
            teamMember.TeamId,
            req.ChallengeId,
            stream,
            file.FileName,
            ct);

        await backgroundTaskQueue.EnqueueAsync(
            req.Id,
            BackgroundTaskTypes.AwdpPatchValidation,
            new AwdpPatchValidationPayload(submissionId),
            ct);

        await SendAsync(new SubmitPatchResponse
        {
            SubmissionId = submissionId,
            Status = "pending"
        }, 202, ct);
    }
}
