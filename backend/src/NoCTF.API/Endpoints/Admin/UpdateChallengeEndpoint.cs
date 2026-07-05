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
    public string? OrchestrationJson { get; set; }
    public string? FlagSecret { get; set; }
    public string? FlagEnvironmentVariable { get; set; }
    public string? AttachmentUrl { get; set; }
    public string? PatchTemplateUrl { get; set; }
    public ChallengeDeploymentType DeploymentType { get; set; } = ChallengeDeploymentType.NoAttachment;
    public int? ExposedPort { get; set; }
    public CheckerConfigDto? CheckerConfig { get; set; }
    public string? PenetrationConfigJson { get; set; }
}

public class UpdateChallengeEndpoint(ApplicationDbContext db) : Endpoint<UpdateChallengeRequest, ChallengeTemplateAdminDto>, IAuditableEndpoint
{
    public override void Configure()
    {
        Put("/api/admin/challenges/{id}");
        Roles("Admin");
    }

    public override async Task HandleAsync(UpdateChallengeRequest req, CancellationToken ct)
    {
        var challenge = await db.ChallengeTemplates.FirstOrDefaultAsync(c => c.Id == req.Id, ct);

        if (challenge is null)
        {
            await SendNotFoundAsync(ct);
            return;
        }

        var attachmentUrl = ChallengeTemplateRequestRules.CleanOptional(req.AttachmentUrl);
        var isPenetration = string.Equals(req.TypeId?.Trim(), "Penetration", StringComparison.OrdinalIgnoreCase);
        var deploymentType = isPenetration
            ? ChallengeDeploymentType.DynamicContainer
            : ChallengeTemplateRequestRules.ResolveDeploymentType(req.DeploymentType, attachmentUrl);
        var usesRuntimeContainer = ChallengeTemplateRequestRules.UsesRuntimeContainer(deploymentType);
        var containerImage = ChallengeTemplateRequestRules.CleanOptional(req.ContainerImage);
        if (!ChallengeTemplateRequestRules.IsValidJsonObject(req.OrchestrationJson))
        {
            await SendStringAsync("invalid_orchestration_json", 400, cancellation: ct);
            return;
        }

        var requestedOrchestrationJson = string.IsNullOrWhiteSpace(req.OrchestrationJson)
            ? challenge.OrchestrationJson
            : req.OrchestrationJson;
        var orchestrationJson = usesRuntimeContainer || isPenetration
            ? ChallengeTemplateRequestRules.BuildOrchestrationJson(
                requestedOrchestrationJson,
                containerImage,
                req.ExposedPort,
                req.ComposeYaml,
                req.ComposeProjectName,
                isPenetration ? ChallengeContainerMode.DockerCompose : req.ContainerMode)
            : "{}";
        var resolvedImage = ChallengeTemplateRequestRules.ResolveImage(orchestrationJson, containerImage);
        if (usesRuntimeContainer && !isPenetration && string.IsNullOrWhiteSpace(resolvedImage))
        {
            await SendStringAsync("container_image_required", 400, cancellation: ct);
            return;
        }

        challenge.Title = req.Title.Trim();
        challenge.Description = ChallengeTemplateRequestRules.CleanOptional(req.Description);
        challenge.TypeId = string.IsNullOrWhiteSpace(req.TypeId) ? "ctf" : req.TypeId.Trim();
        challenge.ContainerImage = usesRuntimeContainer && !isPenetration ? resolvedImage : null;
        challenge.ContainerMode = isPenetration ? ChallengeContainerMode.DockerCompose : usesRuntimeContainer ? req.ContainerMode : ChallengeContainerMode.SingleImage;
        challenge.ComposeYaml = usesRuntimeContainer ? ChallengeTemplateRequestRules.CleanOptional(req.ComposeYaml) : null;
        challenge.ComposeProjectName = usesRuntimeContainer ? ChallengeTemplateRequestRules.CleanOptional(req.ComposeProjectName) : null;
        challenge.OrchestrationJson = orchestrationJson;
        challenge.FlagSecret = req.FlagSecret;
        challenge.FlagEnvironmentVariable = string.IsNullOrWhiteSpace(req.FlagEnvironmentVariable)
            ? "NOCTF_FLAG_UUID"
            : req.FlagEnvironmentVariable.Trim();
        challenge.AttachmentUrl = attachmentUrl;
        challenge.PatchTemplateUrl = ChallengeTemplateRequestRules.CleanOptional(req.PatchTemplateUrl);
        challenge.DeploymentType = deploymentType;
        challenge.ExposedPort = usesRuntimeContainer ? req.ExposedPort : null;
        challenge.UpdatedAt = DateTime.UtcNow;
        challenge.CheckerConfig = usesRuntimeContainer && req.CheckerConfig is not null
            ? new CheckerConfig
            {
                Image = ChallengeTemplateRequestRules.CleanOptional(req.CheckerConfig.Image),
                Command = ChallengeTemplateRequestRules.CleanOptional(req.CheckerConfig.Command),
                TimeoutSeconds = req.CheckerConfig.TimeoutSeconds,
                ExpImage = ChallengeTemplateRequestRules.CleanOptional(req.CheckerConfig.ExpImage),
                ExpCommand = ChallengeTemplateRequestRules.CleanOptional(req.CheckerConfig.ExpCommand),
            }
            : null;
        challenge.PenetrationConfigJson = isPenetration && !string.IsNullOrWhiteSpace(req.PenetrationConfigJson)
            ? req.PenetrationConfigJson
            : "{}";

        await db.SaveChangesAsync(ct);

        await SendAsync(ChallengeAdminMapping.ToTemplateDto(challenge), cancellation: ct);
    }
}
