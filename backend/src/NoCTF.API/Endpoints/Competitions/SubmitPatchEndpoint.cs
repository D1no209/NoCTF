using System.Security.Claims;
using FastEndpoints;
using Microsoft.EntityFrameworkCore;
using NoCTF.Application.CompetitionModes;
using NoCTF.Core;
using NoCTF.Infrastructure;

namespace NoCTF.API.Endpoints.Competitions;

public class SubmitPatchRequest
{
    public Guid Id { get; set; }
    public Guid ChallengeId { get; set; }
}

public class SubmitPatchResponse
{
    public Guid? SubmissionId { get; set; }
    public string Status { get; set; } = string.Empty;
    public int DefenseAttempts { get; set; }
    public int MaxDefenseAttempts { get; set; }
}

/// <summary>
/// POST /api/competitions/{id}/challenges/{challengeId}/patch
/// Accepts multipart/form-data and delegates the patch-like file action to the active competition mode.
/// </summary>
public class SubmitPatchEndpoint(
    ApplicationDbContext dbContext,
    IEnumerable<ICompetitionFileActionProvider> fileActionProviders,
    IConfiguration configuration)
    : Endpoint<SubmitPatchRequest, SubmitPatchResponse>
{
    private static readonly HashSet<string> AllowedContentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "application/gzip",
        "application/x-gzip",
        "application/zip",
        "application/x-zip-compressed",
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

        var competition = await dbContext.Competitions
            .IgnoreQueryFilters()
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == req.Id, ct);
        if (competition is null)
        {
            await SendNotFoundAsync(ct);
            return;
        }

        var modeKey = string.IsNullOrWhiteSpace(competition.ModeKey)
            ? competition.GameModeType.ToString()
            : competition.ModeKey;
        var provider = fileActionProviders.FirstOrDefault(p =>
            string.Equals(p.ModeKey, modeKey, StringComparison.OrdinalIgnoreCase) &&
            p.CanHandleFileAction("submit-patch"));
        if (provider is null)
        {
            await SendAsync(new SubmitPatchResponse { Status = "unsupported_patch_action" }, 404, ct);
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
            !file.FileName.EndsWith(".tgz", StringComparison.OrdinalIgnoreCase) &&
            !file.FileName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
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
        var result = await provider.HandleFileActionAsync(
            new CompetitionFileActionContext(
                req.Id,
                teamMember.TeamId,
                userId,
                req.ChallengeId,
                "submit-patch",
                stream,
                file.FileName,
                file.ContentType,
                HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown"),
            ct);

        if (!result.Success)
        {
            await SendAsync(new SubmitPatchResponse
            {
                SubmissionId = GetGuidDataValue(result.Data, "SubmissionId"),
                Status = result.Code,
                DefenseAttempts = GetIntDataValue(result.Data, "DefenseAttempts"),
                MaxDefenseAttempts = GetIntDataValue(result.Data, "MaxDefenseAttempts")
            }, 400, ct);
            return;
        }

        await SendAsync(new SubmitPatchResponse
        {
            SubmissionId = GetGuidDataValue(result.Data, "SubmissionId"),
            Status = result.Code,
            DefenseAttempts = GetIntDataValue(result.Data, "DefenseAttempts"),
            MaxDefenseAttempts = GetIntDataValue(result.Data, "MaxDefenseAttempts")
        }, 202, ct);
    }

    private static Guid? GetGuidDataValue(object? data, string propertyName)
    {
        var value = GetDataProperty(data, propertyName);
        if (value is Guid guid)
            return guid;

        return Guid.TryParse(value?.ToString(), out var parsed) ? parsed : null;
    }

    private static int GetIntDataValue(object? data, string propertyName)
    {
        var value = GetDataProperty(data, propertyName);
        if (value is int number)
            return number;

        return int.TryParse(value?.ToString(), out var parsed) ? parsed : 0;
    }

    private static object? GetDataProperty(object? data, string propertyName)
        => data?.GetType().GetProperty(propertyName)?.GetValue(data);
}
