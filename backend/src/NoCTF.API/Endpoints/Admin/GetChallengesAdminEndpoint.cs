using FastEndpoints;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using NoCTF.Core;
using NoCTF.Infrastructure;
using NoCTF.PluginBase;

namespace NoCTF.API.Endpoints.Admin;

public class ChallengeTemplateAdminDto
{
    public Guid Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string TypeId { get; set; } = string.Empty;
    public string Direction { get; set; } = string.Empty;
    public string? ContainerImage { get; set; }
    public ChallengeContainerMode ContainerMode { get; set; } = ChallengeContainerMode.SingleImage;
    public string? ComposeYaml { get; set; }
    public string? ComposeProjectName { get; set; }
    public string OrchestrationJson { get; set; } = "{}";
    public string? AttachmentUrl { get; set; }
    public string? PatchTemplateUrl { get; set; }
    public ChallengeDeploymentType DeploymentType { get; set; } = ChallengeDeploymentType.NoAttachment;
    public int? ExposedPort { get; set; }
    public string FlagEnvironmentVariable { get; set; } = "NOCTF_FLAG_UUID";
    public CheckerConfigDto? CheckerConfig { get; set; }
    public string PenetrationConfigJson { get; set; } = "{}";
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class CompetitionChallengeAdminDto
{
    public Guid Id { get; set; }
    public Guid CompetitionId { get; set; }
    public Guid? TemplateId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string DescriptionFormat { get; set; } = "markdown";
    public string TypeId { get; set; } = string.Empty;
    public string Direction { get; set; } = string.Empty;
    public string? ContainerImage { get; set; }
    public ChallengeContainerMode ContainerMode { get; set; } = ChallengeContainerMode.SingleImage;
    public string? ComposeYaml { get; set; }
    public string? ComposeProjectName { get; set; }
    public string OrchestrationJson { get; set; } = "{}";
    public string? AttachmentUrl { get; set; }
    public string? PatchTemplateUrl { get; set; }
    public ChallengeDeploymentType DeploymentType { get; set; } = ChallengeDeploymentType.NoAttachment;
    public int? ExposedPort { get; set; }
    public string FlagPrefix { get; set; } = "flag";
    public string FlagEnvironmentVariable { get; set; } = "NOCTF_FLAG_UUID";
    public CheckerConfigDto? CheckerConfig { get; set; }
    public string PenetrationConfigJson { get; set; } = "{}";
    public PointsConfigDto PointsConfig { get; set; } = new();
    public double DifficultyCoefficient { get; set; } = 1.0;
    public bool EnableBloodBonus { get; set; }
    public int? AwdpAttackScorePerRound { get; set; }
    public int? AwdpDefenseScorePerRound { get; set; }
    public int? AwdpMaxAttackAttempts { get; set; }
    public int? AwdpMaxDefenseAttempts { get; set; }
    public string? AwdpFixEntry { get; set; }
    public int? AwdpFixTimeoutSeconds { get; set; }
    public List<ChallengeHintDto> Hints { get; set; } = [];
}

public class CheckerConfigDto
{
    public string? Image { get; set; }
    public string? Command { get; set; }
    public int? TimeoutSeconds { get; set; }
    public string? ExpImage { get; set; }
    public string? ExpCommand { get; set; }
}

public class PointsConfigDto
{
    public int InitialPoints { get; set; } = 500;
    public int MinimumPoints { get; set; } = 100;
    public int DecayFactor { get; set; } = 450;
    public string DecayFunction { get; set; } = "sigmoid";
}

public class ChallengeHintDto
{
    public Guid Id { get; set; }
    public string Content { get; set; } = string.Empty;
    public int DisplayOrder { get; set; }
}

public static class ChallengeAdminMapping
{
    public static ChallengeTemplateAdminDto ToTemplateDto(ChallengeTemplate challenge, bool includeSensitive = true) => new()
    {
        Id = challenge.Id,
        Title = challenge.Title,
        Description = challenge.Description,
        TypeId = challenge.TypeId,
        Direction = challenge.Direction,
        ContainerImage = challenge.ContainerImage,
        ContainerMode = challenge.ContainerMode,
        ComposeYaml = includeSensitive ? challenge.ComposeYaml : null,
        ComposeProjectName = includeSensitive ? challenge.ComposeProjectName : null,
        OrchestrationJson = !includeSensitive || string.IsNullOrWhiteSpace(challenge.OrchestrationJson)
            ? "{}"
            : challenge.OrchestrationJson,
        AttachmentUrl = challenge.AttachmentUrl,
        PatchTemplateUrl = challenge.PatchTemplateUrl,
        DeploymentType = challenge.DeploymentType,
        ExposedPort = challenge.ExposedPort,
        FlagEnvironmentVariable = string.IsNullOrWhiteSpace(challenge.FlagEnvironmentVariable)
            ? "NOCTF_FLAG_UUID"
            : challenge.FlagEnvironmentVariable,
        CheckerConfig = !includeSensitive || challenge.CheckerConfig is null ? null : new CheckerConfigDto
        {
            Image = challenge.CheckerConfig.Image,
            Command = challenge.CheckerConfig.Command,
            TimeoutSeconds = challenge.CheckerConfig.TimeoutSeconds,
            ExpImage = challenge.CheckerConfig.ExpImage,
            ExpCommand = challenge.CheckerConfig.ExpCommand,
        },
        PenetrationConfigJson = !includeSensitive || string.IsNullOrWhiteSpace(challenge.PenetrationConfigJson)
            ? "{}"
            : challenge.PenetrationConfigJson,
        CreatedAt = challenge.CreatedAt,
        UpdatedAt = challenge.UpdatedAt,
    };

    public static CompetitionChallengeAdminDto ToCompetitionDto(Challenge challenge, IEnumerable<ChallengeHint> hints) => new()
    {
        Id = challenge.Id,
        CompetitionId = challenge.CompetitionId,
        TemplateId = challenge.TemplateId,
        Title = challenge.Title,
        Description = challenge.Description,
        DescriptionFormat = string.IsNullOrWhiteSpace(challenge.DescriptionFormat) ? "markdown" : challenge.DescriptionFormat,
        TypeId = challenge.TypeId,
        Direction = challenge.Direction,
        ContainerImage = challenge.ContainerImage,
        ContainerMode = challenge.ContainerMode,
        ComposeYaml = challenge.ComposeYaml,
        ComposeProjectName = challenge.ComposeProjectName,
        OrchestrationJson = string.IsNullOrWhiteSpace(challenge.OrchestrationJson)
            ? "{}"
            : challenge.OrchestrationJson,
        AttachmentUrl = challenge.AttachmentUrl,
        PatchTemplateUrl = challenge.PatchTemplateUrl,
        DeploymentType = challenge.DeploymentType,
        ExposedPort = challenge.ExposedPort,
        FlagPrefix = string.IsNullOrWhiteSpace(challenge.FlagPrefix) ? "flag" : challenge.FlagPrefix,
        FlagEnvironmentVariable = string.IsNullOrWhiteSpace(challenge.FlagEnvironmentVariable)
            ? "NOCTF_FLAG_UUID"
            : challenge.FlagEnvironmentVariable,
        CheckerConfig = challenge.CheckerConfig is null ? null : new CheckerConfigDto
        {
            Image = challenge.CheckerConfig.Image,
            Command = challenge.CheckerConfig.Command,
            TimeoutSeconds = challenge.CheckerConfig.TimeoutSeconds,
            ExpImage = challenge.CheckerConfig.ExpImage,
            ExpCommand = challenge.CheckerConfig.ExpCommand,
        },
        PenetrationConfigJson = string.IsNullOrWhiteSpace(challenge.PenetrationConfigJson)
            ? "{}"
            : challenge.PenetrationConfigJson,
        PointsConfig = new PointsConfigDto
        {
            InitialPoints = challenge.PointsConfig.InitialPoints,
            MinimumPoints = challenge.PointsConfig.MinimumPoints,
            DecayFactor = challenge.PointsConfig.DecayFactor,
            DecayFunction = challenge.PointsConfig.DecayFunction,
        },
        DifficultyCoefficient = challenge.DifficultyCoefficient,
        EnableBloodBonus = challenge.EnableBloodBonus,
        AwdpAttackScorePerRound = challenge.AwdpAttackScorePerRound,
        AwdpDefenseScorePerRound = challenge.AwdpDefenseScorePerRound,
        AwdpMaxAttackAttempts = challenge.AwdpMaxAttackAttempts,
        AwdpMaxDefenseAttempts = challenge.AwdpMaxDefenseAttempts,
        AwdpFixEntry = challenge.AwdpFixEntry,
        AwdpFixTimeoutSeconds = challenge.AwdpFixTimeoutSeconds,
        Hints = hints
            .OrderBy(h => h.DisplayOrder)
            .Select(h => new ChallengeHintDto
            {
                Id = h.Id,
                Content = h.Content,
                DisplayOrder = h.DisplayOrder,
            })
            .ToList(),
    };
}

public class GetChallengesAdminEndpoint(ApplicationDbContext db, IStorageProvider storageProvider) : Endpoint<EmptyRequest, List<ChallengeTemplateAdminDto>>
{
    public override void Configure()
    {
        Get("/api/admin/challenges");
        Roles("Admin", "Organizer");
    }

    public override async Task HandleAsync(EmptyRequest req, CancellationToken ct)
    {
        var challenges = await db.ChallengeTemplates
            .AsNoTracking()
            .OrderBy(c => c.Title)
            .ToListAsync(ct);

        var includeSensitive = User.IsInRole(UserRole.Admin.ToString()) ||
                               string.Equals(
                                   User.FindFirst(ClaimTypes.Role)?.Value,
                                   UserRole.Admin.ToString(),
                                   StringComparison.OrdinalIgnoreCase);
        var response = new List<ChallengeTemplateAdminDto>(challenges.Count);
        foreach (var challenge in challenges)
        {
            var dto = ChallengeAdminMapping.ToTemplateDto(challenge, includeSensitive);
            dto.AttachmentUrl = await StorageUrlResolver.ResolveAsync(
                storageProvider, challenge.AttachmentStorageKey, dto.AttachmentUrl, ct);
            dto.PatchTemplateUrl = await StorageUrlResolver.ResolveAsync(
                storageProvider, challenge.PatchTemplateStorageKey, dto.PatchTemplateUrl, ct);
            response.Add(dto);
        }

        await SendAsync(response, cancellation: ct);
    }
}
