using FastEndpoints;
using Microsoft.EntityFrameworkCore;
using NoCTF.Core;
using NoCTF.Infrastructure;

namespace NoCTF.API.Endpoints.Admin;

public class UpdateChallengeRequest
{
    public Guid Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string TypeId { get; set; } = "ctf";
    public string? ContainerImage { get; set; }
    public ChallengeContainerMode ContainerMode { get; set; } = ChallengeContainerMode.SingleImage;
    public string? ComposeYaml { get; set; }
    public string? ComposeProjectName { get; set; }
    public string? FlagSecret { get; set; }
    public string? FlagEnvironmentVariable { get; set; }
    public string? AttachmentUrl { get; set; }
    public ChallengeDeploymentType DeploymentType { get; set; } = ChallengeDeploymentType.NoAttachment;
    public int? ExposedPort { get; set; }
    public CheckerConfigDto? CheckerConfig { get; set; }
}

public class UpdateChallengeEndpoint(ApplicationDbContext db) : Endpoint<UpdateChallengeRequest, ChallengeTemplateAdminDto>, IAuditableEndpoint
{
    public override void Configure()
    {
        Put("/api/admin/challenges/{id}");
        Roles("Admin", "Organizer");
    }

    public override async Task HandleAsync(UpdateChallengeRequest req, CancellationToken ct)
    {
        var challenge = await db.ChallengeTemplates.FirstOrDefaultAsync(c => c.Id == req.Id, ct);

        if (challenge is null)
        {
            await SendNotFoundAsync(ct);
            return;
        }

        challenge.Title = req.Title.Trim();
        challenge.Description = req.Description;
        challenge.TypeId = string.IsNullOrWhiteSpace(req.TypeId) ? "ctf" : req.TypeId.Trim();
        challenge.ContainerImage = req.ContainerImage;
        challenge.ContainerMode = req.ContainerMode;
        challenge.ComposeYaml = req.ComposeYaml;
        challenge.ComposeProjectName = req.ComposeProjectName;
        challenge.FlagSecret = req.FlagSecret;
        challenge.FlagEnvironmentVariable = string.IsNullOrWhiteSpace(req.FlagEnvironmentVariable)
            ? "NOCTF_FLAG_UUID"
            : req.FlagEnvironmentVariable.Trim();
        challenge.AttachmentUrl = req.AttachmentUrl;
        challenge.DeploymentType = req.DeploymentType;
        challenge.ExposedPort = req.ExposedPort;
        challenge.UpdatedAt = DateTime.UtcNow;
        challenge.CheckerConfig = req.CheckerConfig is not null
            ? new CheckerConfig { Image = req.CheckerConfig.Image, Command = req.CheckerConfig.Command }
            : null;

        await db.SaveChangesAsync(ct);

        await SendAsync(ChallengeAdminMapping.ToTemplateDto(challenge), cancellation: ct);
    }
}
