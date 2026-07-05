using FastEndpoints;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using NoCTF.API.Endpoints.Competitions;
using NoCTF.API.Permissions;
using NoCTF.Application;
using NoCTF.Application.CompetitionModes;
using NoCTF.Application.Leaderboard;
using NoCTF.Application.Scoring;
using NoCTF.Core;
using NoCTF.Infrastructure;
using NoCTF.PluginBase;

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
    public string? OrchestrationJson { get; set; }
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
    public string? OrchestrationJson { get; set; }
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

        if (!ChallengeTemplateRequestRules.IsValidJsonObject(req.OrchestrationJson))
        {
            await SendStringAsync("invalid_orchestration_json", 400, cancellation: ct);
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
            OrchestrationJson = string.IsNullOrWhiteSpace(req.OrchestrationJson)
                ? template.OrchestrationJson
                : req.OrchestrationJson,
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

public class UpdateCompetitionChallengeEndpoint(
    ApplicationDbContext db,
    ICompetitionPermissionService permissions,
    ICtfScoreRebuilder ctfScoreRebuilder)
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
        if (!ChallengeTemplateRequestRules.IsValidJsonObject(req.OrchestrationJson))
        {
            await SendStringAsync("invalid_orchestration_json", 400, cancellation: ct);
            return;
        }
        if (!string.IsNullOrWhiteSpace(req.OrchestrationJson))
            challenge.OrchestrationJson = req.OrchestrationJson;
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
        await ctfScoreRebuilder.RebuildChallengeAsync(competitionId, challengeId, ct);
        await SendAsync(ChallengeAdminMapping.ToCompetitionDto(challenge, hints), cancellation: ct);
    }
}

public class DeleteCompetitionChallengeEndpoint(
    ApplicationDbContext db,
    ICompetitionPermissionService permissions,
    ILeaderboardService leaderboardService,
    IRedisLeaderboardCache leaderboardCache,
    IHubNotifierService hubNotifier,
    IContainerManager containerManager)
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

        Guid? userId = null;
        if (Guid.TryParse(User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value, out var parsedUserId))
            userId = parsedUserId;

        await DeleteChallengeArtifactsAsync(db, containerManager, challenge, HttpContext, userId, ct);

        db.Challenges.Remove(challenge);
        await db.SaveChangesAsync(ct);
        await RefreshLeaderboardAsync(competitionId, ct);
        await SendNoContentAsync(ct);
    }

    public static async Task DeleteChallengeArtifactsAsync(
        ApplicationDbContext db,
        IContainerManager containerManager,
        Challenge challenge,
        HttpContext? httpContext,
        Guid? userId,
        CancellationToken ct)
    {
        var competitionId = challenge.CompetitionId;
        var challengeId = challenge.Id;

        var boxes = await db.AwdGameBoxes
            .IgnoreQueryFilters()
            .Where(g => g.CompetitionId == competitionId && g.ChallengeId == challengeId)
            .ToListAsync(ct);
        foreach (var box in boxes.Where(box => !string.IsNullOrWhiteSpace(box.ContainerInstanceId)))
        {
            await CreateChallengeInstanceEndpoint.DestroyTrackedBoxAsync(
                db,
                containerManager,
                box,
                httpContext,
                "challenge_deleted",
                userId,
                updateCooldown: false,
                ct);
        }
        db.AwdGameBoxes.RemoveRange(boxes);

        var teamInstances = await db.TeamChallengeInstances
            .IgnoreQueryFilters()
            .Where(i => i.CompetitionId == competitionId && i.ChallengeId == challengeId)
            .ToListAsync(ct);
        foreach (var instance in teamInstances.Where(i => !string.IsNullOrWhiteSpace(i.ComposeProjectName)))
        {
            await containerManager.ComposeDownAsync(new ComposeDeployment(
                Id: Guid.NewGuid(),
                CompetitionId: instance.CompetitionId,
                TeamId: instance.TeamId,
                ChallengeId: instance.ChallengeId,
                ProviderType: "compose",
                ProjectName: instance.ComposeProjectName!,
                ComposeYaml: string.IsNullOrWhiteSpace(instance.RenderedComposeYaml)
                    ? "services:\n  cleanup:\n    image: scratch\n"
                    : instance.RenderedComposeYaml,
                Status: instance.Status.ToString().ToLowerInvariant(),
                StartedAt: instance.CreatedAt,
                ExpectedStopAt: instance.ExpiresAt), ct);
            CompetitionLogWriter.Add(
                db,
                competitionId,
                "penetration.instance.destroyed",
                "Penetration range instance was destroyed because the challenge was deleted.",
                teamId: instance.TeamId,
                userId: userId,
                challengeId: challengeId,
                metadata: new { instanceId = instance.Id, reason = "challenge_deleted" });
        }
        db.TeamChallengeInstances.RemoveRange(teamInstances);

        var hints = await db.ChallengeHints
            .IgnoreQueryFilters()
            .Where(h => h.CompetitionId == competitionId && h.ChallengeId == challengeId)
            .ToListAsync(ct);
        db.ChallengeHints.RemoveRange(hints);

        var penetrationTopologies = await db.PenetrationTopologies
            .IgnoreQueryFilters()
            .Where(t => t.CompetitionId == competitionId && t.ChallengeId == challengeId)
            .ToListAsync(ct);
        var penetrationTopologyIds = penetrationTopologies.Select(t => t.Id).ToList();

        var penetrationFlags = await db.PenetrationFlags
            .IgnoreQueryFilters()
            .Where(f =>
                f.CompetitionId == competitionId &&
                (f.ChallengeId == challengeId || penetrationTopologyIds.Contains(f.TopologyId)))
            .ToListAsync(ct);
        var penetrationFlagIds = penetrationFlags.Select(f => f.Id).ToList();

        var submissions = await db.Submissions
            .IgnoreQueryFilters()
            .Where(s => s.CompetitionId == competitionId && s.ChallengeId == challengeId)
            .ToListAsync(ct);
        db.Submissions.RemoveRange(submissions);

        var scoreEvents = await db.ScoreEvents
            .IgnoreQueryFilters()
            .Where(s => s.CompetitionId == competitionId && s.ChallengeId == challengeId)
            .ToListAsync(ct);
        db.ScoreEvents.RemoveRange(scoreEvents);

        var scoreSignals = await db.ScoreSignals
            .IgnoreQueryFilters()
            .Where(s =>
                s.CompetitionId == competitionId &&
                (s.SubjectId == challengeId ||
                 (s.SubjectId.HasValue && penetrationFlagIds.Contains(s.SubjectId.Value))))
            .ToListAsync(ct);
        db.ScoreSignals.RemoveRange(scoreSignals);

        var dynamicFlags = await db.CtfDynamicFlags
            .IgnoreQueryFilters()
            .Where(f => f.CompetitionId == competitionId && f.ChallengeId == challengeId)
            .ToListAsync(ct);
        db.CtfDynamicFlags.RemoveRange(dynamicFlags);

        var dynamicFlagInstances = await db.DynamicFlagInstances
            .IgnoreQueryFilters()
            .Where(f => f.CompetitionId == competitionId && f.ChallengeId == challengeId)
            .ToListAsync(ct);
        db.DynamicFlagInstances.RemoveRange(dynamicFlagInstances);

        var cheatIncidents = await db.CheatIncidents
            .IgnoreQueryFilters()
            .Where(i => i.CompetitionId == competitionId && i.ChallengeId == challengeId)
            .ToListAsync(ct);
        db.CheatIncidents.RemoveRange(cheatIncidents);

        var awdFlags = await db.AwdFlags
            .IgnoreQueryFilters()
            .Where(f => f.CompetitionId == competitionId && f.ChallengeId == challengeId)
            .ToListAsync(ct);
        db.AwdFlags.RemoveRange(awdFlags);

        var awdAttackRecords = await db.AwdAttackRecords
            .IgnoreQueryFilters()
            .Where(r => r.CompetitionId == competitionId && r.ChallengeId == challengeId)
            .ToListAsync(ct);
        db.AwdAttackRecords.RemoveRange(awdAttackRecords);

        var awdCheckResults = await db.AwdCheckResults
            .IgnoreQueryFilters()
            .Where(r => r.CompetitionId == competitionId && r.ChallengeId == challengeId)
            .ToListAsync(ct);
        db.AwdCheckResults.RemoveRange(awdCheckResults);

        var awdpStates = await db.AwdpTeamChallengeStates
            .IgnoreQueryFilters()
            .Where(s => s.CompetitionId == competitionId && s.ChallengeId == challengeId)
            .ToListAsync(ct);
        db.AwdpTeamChallengeStates.RemoveRange(awdpStates);

        var awdpRoundScores = await db.AwdpRoundScores
            .IgnoreQueryFilters()
            .Where(s => s.CompetitionId == competitionId && s.ChallengeId == challengeId)
            .ToListAsync(ct);
        db.AwdpRoundScores.RemoveRange(awdpRoundScores);

        var awdpPatchSubmissions = await db.AwdpPatchSubmissions
            .IgnoreQueryFilters()
            .Where(s => s.CompetitionId == competitionId && s.ChallengeId == challengeId)
            .ToListAsync(ct);
        await RemoveAwdpPatchValidationTasksAsync(db, competitionId, awdpPatchSubmissions.Select(s => s.Id).ToList(), ct);
        db.AwdpPatchSubmissions.RemoveRange(awdpPatchSubmissions);

        var penetrationNodes = await db.PenetrationNodes
            .IgnoreQueryFilters()
            .Where(n => n.CompetitionId == competitionId && penetrationTopologyIds.Contains(n.TopologyId))
            .ToListAsync(ct);
        db.PenetrationNodes.RemoveRange(penetrationNodes);
        db.PenetrationFlags.RemoveRange(penetrationFlags);
        db.PenetrationTopologies.RemoveRange(penetrationTopologies);

        var kohControlRecords = await db.KohControlRecords
            .IgnoreQueryFilters()
            .Where(r => r.CompetitionId == competitionId && r.ChallengeId == challengeId)
            .ToListAsync(ct);
        db.KohControlRecords.RemoveRange(kohControlRecords);
    }

    private async Task RefreshLeaderboardAsync(Guid competitionId, CancellationToken ct)
    {
        var entries = await leaderboardService.CalculateLeaderboardAsync(competitionId, ct);
        await leaderboardCache.UpdateAsync(competitionId, entries, ct);
        await hubNotifier.NotifyLeaderboardSnapshotAsync(
            competitionId,
            entries.Select(e => new LeaderboardEntryPayload(
                e.Rank,
                e.TeamId,
                e.TeamName,
                e.TotalScore,
                e.SolvedCount)),
            ct);
    }

    private static async Task RemoveAwdpPatchValidationTasksAsync(
        ApplicationDbContext db,
        Guid competitionId,
        IReadOnlyCollection<Guid> submissionIds,
        CancellationToken ct)
    {
        if (submissionIds.Count == 0)
            return;

        var submissionIdText = submissionIds.Select(id => id.ToString()).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var tasks = await db.BackgroundTasks
            .IgnoreQueryFilters()
            .Where(t => t.CompetitionId == competitionId && t.Type == "awdp.patch.validation")
            .ToListAsync(ct);
        db.BackgroundTasks.RemoveRange(tasks.Where(t =>
            submissionIdText.Any(id => t.PayloadJson.Contains(id, StringComparison.OrdinalIgnoreCase))));
    }
}
