using FastEndpoints;
using NoCTF.Core;
using NoCTF.Infrastructure;

namespace NoCTF.API.Endpoints.Admin;

public class CreateChallengeRequest
{
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

public class CreateChallengeEndpoint(ApplicationDbContext db) : Endpoint<CreateChallengeRequest, ChallengeTemplateAdminDto>, IAuditableEndpoint
{
    public override void Configure()
    {
        Post("/api/admin/challenges");
        Roles("Admin", "Organizer");
    }

    public override async Task HandleAsync(CreateChallengeRequest req, CancellationToken ct)
    {
        var challenge = new ChallengeTemplate
        {
            Id = Guid.NewGuid(),
            Title = req.Title.Trim(),
            Description = req.Description,
            TypeId = string.IsNullOrWhiteSpace(req.TypeId) ? "ctf" : req.TypeId.Trim(),
            ContainerImage = req.ContainerImage,
            ContainerMode = req.ContainerMode,
            ComposeYaml = req.ComposeYaml,
            ComposeProjectName = req.ComposeProjectName,
            FlagSecret = req.FlagSecret,
            FlagEnvironmentVariable = string.IsNullOrWhiteSpace(req.FlagEnvironmentVariable)
                ? "NOCTF_FLAG_UUID"
                : req.FlagEnvironmentVariable.Trim(),
            AttachmentUrl = req.AttachmentUrl,
            DeploymentType = req.DeploymentType,
            ExposedPort = req.ExposedPort,
            CheckerConfig = req.CheckerConfig is not null
                ? new CheckerConfig { Image = req.CheckerConfig.Image, Command = req.CheckerConfig.Command }
                : null,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };

        db.ChallengeTemplates.Add(challenge);
        await db.SaveChangesAsync(ct);

        await SendAsync(ChallengeAdminMapping.ToTemplateDto(challenge), 201, ct);
    }
}
