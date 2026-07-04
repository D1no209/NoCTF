using FastEndpoints;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using NoCTF.API.Permissions;
using NoCTF.Application.CompetitionModes;
using NoCTF.Core;
using NoCTF.Infrastructure;

namespace NoCTF.API.Endpoints.Admin;

public class BindCompetitionChallengeRequest
{
    public Guid CompetitionId { get; set; }
    public Guid TemplateId { get; set; }
    public string? Description { get; set; }
    public string DescriptionFormat { get; set; } = "markdown";
    public string? FlagPrefix { get; set; }
    public PointsConfigDto? PointsConfig { get; set; }
    public double DifficultyCoefficient { get; set; } = 1.0;
    public bool EnableBloodBonus { get; set; }
    public int? AwdpAttackScorePerRound { get; set; }
    public int? AwdpDefenseScorePerRound { get; set; }
    public int? AwdpMaxAttackAttempts { get; set; }
    public int? AwdpMaxDefenseAttempts { get; set; }
    public string? AwdpFixEntry { get; set; }
    public int? AwdpFixTimeoutSeconds { get; set; }
    public List<string> Hints { get; set; } = [];
}

public class UpdateCompetitionChallengeRequest
{
    public Guid CompetitionId { get; set; }
    public Guid ChallengeId { get; set; }
    public string? Description { get; set; }
    public string DescriptionFormat { get; set; } = "markdown";
    public string? FlagPrefix { get; set; }
    public PointsConfigDto? PointsConfig { get; set; }
    public double DifficultyCoefficient { get; set; } = 1.0;
    public bool EnableBloodBonus { get; set; }
    public int? AwdpAttackScorePerRound { get; set; }
    public int? AwdpDefenseScorePerRound { get; set; }
    public int? AwdpMaxAttackAttempts { get; set; }
    public int? AwdpMaxDefenseAttempts { get; set; }
    public string? AwdpFixEntry { get; set; }
    public int? AwdpFixTimeoutSeconds { get; set; }
    public List<string> Hints { get; set; } = [];
}

public class GetCompetitionChallengesAdminEndpoint(ApplicationDbContext db, ICompetitionPermissionService permissions)
    : EndpointWithoutRequest<List<CompetitionChallengeAdminDto>>
{
    public override void Configure()
    {
        Get("/api/admin/competitions/{competitionId}/challenges");
        Roles("Admin", "Organizer");
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var competitionId = Route<Guid>("competitionId");
        if (!await AdminCompetitionAuthorization.CanManageAsync(HttpContext, permissions, competitionId, ct))
        {
            await SendForbiddenAsync(ct);
            return;
        }

        var challenges = await db.Challenges
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(c => c.CompetitionId == competitionId)
            .OrderBy(c => c.CreatedAt)
            .ToListAsync(ct);

        var challengeIds = challenges.Select(c => c.Id).ToList();
        var hints = await db.ChallengeHints
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(h => h.CompetitionId == competitionId && challengeIds.Contains(h.ChallengeId))
            .ToListAsync(ct);

        await SendAsync(challenges
            .Select(c => ChallengeAdminMapping.ToCompetitionDto(c, hints.Where(h => h.ChallengeId == c.Id)))
            .ToList(), cancellation: ct);
    }
}

public class BindCompetitionChallengeEndpoint(ApplicationDbContext db, IChallengeAdminFeatureRegistry adminFeatureRegistry, ICompetitionPermissionService permissions)
    : Endpoint<BindCompetitionChallengeRequest, CompetitionChallengeAdminDto>, IAuditableEndpoint
{
    public override void Configure()
    {
        Post("/api/admin/competitions/{competitionId}/challenges");
        Roles("Admin", "Organizer");
    }

    public override async Task HandleAsync(BindCompetitionChallengeRequest req, CancellationToken ct)
    {
        var competitionId = Route<Guid>("competitionId");
        if (!await AdminCompetitionAuthorization.CanManageAsync(HttpContext, permissions, competitionId, ct))
        {
            await SendForbiddenAsync(ct);
            return;
        }

        var competition = await db.Competitions
            .IgnoreQueryFilters()
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == competitionId, ct);
        if (competition is null)
        {
            await SendNotFoundAsync(ct);
            return;
        }

        var template = await db.ChallengeTemplates
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == req.TemplateId, ct);
        if (template is null)
        {
            await SendStringAsync("challenge_template_not_found", 404, cancellation: ct);
            return;
        }

        var isPenetration = string.Equals(template.TypeId, "Penetration", StringComparison.OrdinalIgnoreCase);
        IChallengeAdminFeatureProvider? penetrationProvider = null;
        if (isPenetration)
        {
            penetrationProvider = adminFeatureRegistry.FindProvider(template.TypeId);
            if (penetrationProvider is null)
            {
                await SendStringAsync("challenge_type_plugin_unavailable", 503, cancellation: ct);
                return;
            }
        }

        var points = req.PointsConfig ?? new PointsConfigDto
        {
            InitialPoints = competition.DefaultInitialPoints,
            MinimumPoints = competition.DefaultMinimumPoints,
            DecayFactor = competition.DefaultDecayFactor,
            DecayFunction = competition.DefaultDecayFunction,
        };

        var challenge = new Challenge
        {
            Id = Guid.NewGuid(),
            CompetitionId = competitionId,
            TemplateId = template.Id,
            Title = template.Title,
            Description = string.IsNullOrWhiteSpace(req.Description) ? template.Description : req.Description,
            DescriptionFormat = string.IsNullOrWhiteSpace(req.DescriptionFormat) ? "markdown" : req.DescriptionFormat,
            TypeId = template.TypeId,
            ContainerImage = template.ContainerImage,
            ContainerMode = template.ContainerMode,
            ComposeYaml = template.ComposeYaml,
            ComposeProjectName = template.ComposeProjectName,
            FlagSecret = template.FlagSecret,
            FlagPrefix = string.IsNullOrWhiteSpace(req.FlagPrefix) ? "flag" : req.FlagPrefix.Trim(),
            FlagEnvironmentVariable = string.IsNullOrWhiteSpace(template.FlagEnvironmentVariable)
                ? "NOCTF_FLAG_UUID"
                : template.FlagEnvironmentVariable,
            AttachmentUrl = template.AttachmentUrl,
            PatchTemplateUrl = template.PatchTemplateUrl,
            DeploymentType = template.DeploymentType,
            ExposedPort = template.ExposedPort,
            PointsConfig = new PointsConfig(points.InitialPoints, points.MinimumPoints, points.DecayFactor, points.DecayFunction),
            DifficultyCoefficient = req.DifficultyCoefficient <= 0 ? competition.DifficultyCoefficient : req.DifficultyCoefficient,
            EnableBloodBonus = req.EnableBloodBonus,
            AwdpAttackScorePerRound = req.AwdpAttackScorePerRound,
            AwdpDefenseScorePerRound = req.AwdpDefenseScorePerRound,
            AwdpMaxAttackAttempts = req.AwdpMaxAttackAttempts,
            AwdpMaxDefenseAttempts = req.AwdpMaxDefenseAttempts,
            AwdpFixEntry = string.IsNullOrWhiteSpace(req.AwdpFixEntry) ? null : req.AwdpFixEntry.Trim(),
            AwdpFixTimeoutSeconds = req.AwdpFixTimeoutSeconds,
            CheckerConfig = template.CheckerConfig is not null
                ? new CheckerConfig
                {
                    Image = template.CheckerConfig.Image,
                    Command = template.CheckerConfig.Command,
                    TimeoutSeconds = template.CheckerConfig.TimeoutSeconds,
                    ExpImage = template.CheckerConfig.ExpImage,
                    ExpCommand = template.CheckerConfig.ExpCommand,
                }
                : null,
            PenetrationConfigJson = string.IsNullOrWhiteSpace(template.PenetrationConfigJson)
                ? "{}"
                : template.PenetrationConfigJson,
            CreatedAt = DateTime.UtcNow,
        };

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        db.Challenges.Add(challenge);
        var hints = BuildHints(competitionId, challenge.Id, req.Hints);
        db.ChallengeHints.AddRange(hints);
        await db.SaveChangesAsync(ct);

        if (isPenetration && penetrationProvider is not null)
        {
            var payload = JsonSerializer.Serialize(new { templateId = template.Id, challengeId = challenge.Id }, new JsonSerializerOptions(JsonSerializerDefaults.Web));
            await penetrationProvider.HandleAsync(new ChallengeFeatureContext(
                CompetitionId: competitionId,
                ChallengeId: challenge.Id,
                TeamId: null,
                UserId: Guid.Empty,
                TypeId: template.TypeId,
                FeatureKey: "penetration.template.clone-to-challenge",
                PayloadJson: payload,
                IpAddress: "system"), ct);
        }

        await transaction.CommitAsync(ct);

        await SendAsync(ChallengeAdminMapping.ToCompetitionDto(challenge, hints), 201, ct);
    }

    private static List<ChallengeHint> BuildHints(Guid competitionId, Guid challengeId, IEnumerable<string> hints)
        => hints
            .Where(h => !string.IsNullOrWhiteSpace(h))
            .Select((h, index) => new ChallengeHint
            {
                Id = Guid.NewGuid(),
                CompetitionId = competitionId,
                ChallengeId = challengeId,
                Content = h.Trim(),
                DisplayOrder = index + 1,
                CreatedAt = DateTime.UtcNow,
            })
            .ToList();
}

public class UpdateCompetitionChallengeEndpoint(ApplicationDbContext db, ICompetitionPermissionService permissions)
    : Endpoint<UpdateCompetitionChallengeRequest, CompetitionChallengeAdminDto>, IAuditableEndpoint
{
    public override void Configure()
    {
        Put("/api/admin/competitions/{competitionId}/challenges/{challengeId}");
        Roles("Admin", "Organizer");
    }

    public override async Task HandleAsync(UpdateCompetitionChallengeRequest req, CancellationToken ct)
    {
        var competitionId = Route<Guid>("competitionId");
        var challengeId = Route<Guid>("challengeId");
        if (!await AdminCompetitionAuthorization.CanManageAsync(HttpContext, permissions, competitionId, ct))
        {
            await SendForbiddenAsync(ct);
            return;
        }

        var challenge = await db.Challenges
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(c => c.Id == challengeId && c.CompetitionId == competitionId, ct);

        if (challenge is null)
        {
            await SendNotFoundAsync(ct);
            return;
        }

        challenge.Description = req.Description;
        challenge.DescriptionFormat = string.IsNullOrWhiteSpace(req.DescriptionFormat) ? "markdown" : req.DescriptionFormat;
        challenge.FlagPrefix = string.IsNullOrWhiteSpace(req.FlagPrefix) ? "flag" : req.FlagPrefix.Trim();
        challenge.DifficultyCoefficient = req.DifficultyCoefficient <= 0 ? 1.0 : req.DifficultyCoefficient;
        challenge.EnableBloodBonus = req.EnableBloodBonus;
        challenge.AwdpAttackScorePerRound = req.AwdpAttackScorePerRound;
        challenge.AwdpDefenseScorePerRound = req.AwdpDefenseScorePerRound;
        challenge.AwdpMaxAttackAttempts = req.AwdpMaxAttackAttempts;
        challenge.AwdpMaxDefenseAttempts = req.AwdpMaxDefenseAttempts;
        challenge.AwdpFixEntry = string.IsNullOrWhiteSpace(req.AwdpFixEntry) ? null : req.AwdpFixEntry.Trim();
        challenge.AwdpFixTimeoutSeconds = req.AwdpFixTimeoutSeconds;
        if (req.PointsConfig is not null)
        {
            challenge.PointsConfig = new PointsConfig(
                req.PointsConfig.InitialPoints,
                req.PointsConfig.MinimumPoints,
                req.PointsConfig.DecayFactor,
                req.PointsConfig.DecayFunction);
        }

        var existingHints = await db.ChallengeHints
            .IgnoreQueryFilters()
            .Where(h => h.CompetitionId == competitionId && h.ChallengeId == challengeId)
            .ToListAsync(ct);
        db.ChallengeHints.RemoveRange(existingHints);
        var hints = req.Hints
            .Where(h => !string.IsNullOrWhiteSpace(h))
            .Select((h, index) => new ChallengeHint
            {
                Id = Guid.NewGuid(),
                CompetitionId = competitionId,
                ChallengeId = challengeId,
                Content = h.Trim(),
                DisplayOrder = index + 1,
                CreatedAt = DateTime.UtcNow,
            })
            .ToList();
        db.ChallengeHints.AddRange(hints);

        await db.SaveChangesAsync(ct);
        await SendAsync(ChallengeAdminMapping.ToCompetitionDto(challenge, hints), cancellation: ct);
    }
}

public class DeleteCompetitionChallengeEndpoint(ApplicationDbContext db, ICompetitionPermissionService permissions)
    : EndpointWithoutRequest, IAuditableEndpoint
{
    public override void Configure()
    {
        Delete("/api/admin/competitions/{competitionId}/challenges/{challengeId}");
        Roles("Admin", "Organizer");
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var competitionId = Route<Guid>("competitionId");
        var challengeId = Route<Guid>("challengeId");
        if (!await AdminCompetitionAuthorization.CanManageAsync(HttpContext, permissions, competitionId, ct))
        {
            await SendForbiddenAsync(ct);
            return;
        }

        var challenge = await db.Challenges
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(c => c.Id == challengeId && c.CompetitionId == competitionId, ct);
        if (challenge is null)
        {
            await SendNotFoundAsync(ct);
            return;
        }

        var hints = await db.ChallengeHints
            .IgnoreQueryFilters()
            .Where(h => h.CompetitionId == competitionId && h.ChallengeId == challengeId)
            .ToListAsync(ct);
        db.ChallengeHints.RemoveRange(hints);
        db.Challenges.Remove(challenge);
        await db.SaveChangesAsync(ct);
        await SendNoContentAsync(ct);
    }
}
