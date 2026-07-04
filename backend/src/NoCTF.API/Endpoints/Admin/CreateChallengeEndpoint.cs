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
    public string? PatchTemplateUrl { get; set; }
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
        var attachmentUrl = ChallengeTemplateRequestRules.CleanOptional(req.AttachmentUrl);
        var deploymentType = ChallengeTemplateRequestRules.ResolveDeploymentType(req.DeploymentType, attachmentUrl);
        var usesRuntimeContainer = ChallengeTemplateRequestRules.UsesRuntimeContainer(deploymentType);
        var containerImage = ChallengeTemplateRequestRules.CleanOptional(req.ContainerImage);
        if (usesRuntimeContainer && string.IsNullOrWhiteSpace(containerImage))
        {
            await SendStringAsync("container_image_required", 400, cancellation: ct);
            return;
        }

        var challenge = new ChallengeTemplate
        {
            Id = Guid.NewGuid(),
            Title = req.Title.Trim(),
            Description = ChallengeTemplateRequestRules.CleanOptional(req.Description),
            TypeId = string.IsNullOrWhiteSpace(req.TypeId) ? "ctf" : req.TypeId.Trim(),
            ContainerImage = usesRuntimeContainer ? containerImage : null,
            ContainerMode = usesRuntimeContainer ? req.ContainerMode : ChallengeContainerMode.SingleImage,
            ComposeYaml = usesRuntimeContainer ? ChallengeTemplateRequestRules.CleanOptional(req.ComposeYaml) : null,
            ComposeProjectName = usesRuntimeContainer ? ChallengeTemplateRequestRules.CleanOptional(req.ComposeProjectName) : null,
            FlagSecret = req.FlagSecret,
            FlagEnvironmentVariable = string.IsNullOrWhiteSpace(req.FlagEnvironmentVariable)
                ? "NOCTF_FLAG_UUID"
                : req.FlagEnvironmentVariable.Trim(),
            AttachmentUrl = attachmentUrl,
            PatchTemplateUrl = ChallengeTemplateRequestRules.CleanOptional(req.PatchTemplateUrl),
            DeploymentType = deploymentType,
            ExposedPort = usesRuntimeContainer ? req.ExposedPort : null,
            CheckerConfig = usesRuntimeContainer && req.CheckerConfig is not null
                ? new CheckerConfig
                {
                    Image = ChallengeTemplateRequestRules.CleanOptional(req.CheckerConfig.Image),
                    Command = ChallengeTemplateRequestRules.CleanOptional(req.CheckerConfig.Command),
                    TimeoutSeconds = req.CheckerConfig.TimeoutSeconds,
                    ExpImage = ChallengeTemplateRequestRules.CleanOptional(req.CheckerConfig.ExpImage),
                    ExpCommand = ChallengeTemplateRequestRules.CleanOptional(req.CheckerConfig.ExpCommand),
                }
                : null,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };

        db.ChallengeTemplates.Add(challenge);
        await db.SaveChangesAsync(ct);

        await SendAsync(ChallengeAdminMapping.ToTemplateDto(challenge), 201, ct);
    }
}
