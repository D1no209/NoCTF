using FastEndpoints;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using System.Security.Cryptography;
using System.Text;
using NoCTF.API.Endpoints.Competitions;
using NoCTF.API.Permissions;
using NoCTF.Application;
using NoCTF.Application.BackgroundTasks;
using NoCTF.Application.Events;
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

public class GetCompetitionChallengesAdminEndpoint(
    ApplicationDbContext db,
    ICompetitionPermissionService permissions,
    IStorageProvider storageProvider)
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

        var response = new List<CompetitionChallengeAdminDto>(challenges.Count);
        foreach (var challenge in challenges)
        {
            var dto = ChallengeAdminMapping.ToCompetitionDto(
                challenge, hints.Where(h => h.ChallengeId == challenge.Id));
            await StorageUrlResolver.ResolveAsync(dto, storageProvider, challenge, ct);
            response.Add(dto);
        }
        await SendAsync(response, cancellation: ct);
    }
}

public class BindCompetitionChallengeEndpoint(
    ApplicationDbContext db,
    IChallengeAdminFeatureRegistry adminFeatureRegistry,
    ICompetitionPermissionService permissions,
    IStorageProvider storageProvider,
    ICompetitionNotificationOutbox notificationOutbox)
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
            AttachmentStorageKey = template.AttachmentStorageKey,
            PatchTemplateStorageKey = template.PatchTemplateStorageKey,
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

        var copiedStorageKeys = new List<string>(2);
        try
        {
            challenge.AttachmentStorageKey = await CopyTemplateObjectAsync(
                template.AttachmentStorageKey,
                $"challenge-attachments/{competitionId:N}/{challenge.Id:N}",
                copiedStorageKeys,
                ct);
            challenge.PatchTemplateStorageKey = await CopyTemplateObjectAsync(
                template.PatchTemplateStorageKey,
                $"challenge-patch-templates/{competitionId:N}/{challenge.Id:N}",
                copiedStorageKeys,
                ct);

            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            db.Challenges.Add(challenge);
            var hints = BuildHints(competitionId, challenge.Id, req.Hints);
            db.ChallengeHints.AddRange(hints);
            if (competition.Status == CompetitionStatus.Running)
            {
                notificationOutbox.Add(CompetitionNotification.Create(
                    competitionId, CompetitionNotificationTypes.ChallengePublished,
                    "challenge", challenge.Id, null,
                    $"challenge.published:{challenge.Id:N}:v1",
                    new
                    {
                        problem_title = challenge.Title,
                        problem_category = challenge.TypeId,
                        occurred_at = DateTime.UtcNow.ToString("O")
                    }));
            }
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
            copiedStorageKeys.Clear();

            var response = ChallengeAdminMapping.ToCompetitionDto(challenge, hints);
            await StorageUrlResolver.ResolveAsync(response, storageProvider, challenge, ct);
            await SendAsync(response, 201, ct);
        }
        finally
        {
            var rollbackFailures = new List<string>();
            foreach (var copiedKey in copiedStorageKeys)
            {
                try
                {
                    await storageProvider.DeleteAsync(copiedKey, CancellationToken.None);
                }
                catch
                {
                    rollbackFailures.Add(copiedKey);
                }
            }

            if (rollbackFailures.Count > 0)
            {
                try
                {
                    db.ChangeTracker.Clear();
                    await StorageObjectCleanup.EnqueueAsync(db, rollbackFailures, CancellationToken.None);
                    await db.SaveChangesAsync(CancellationToken.None);
                }
                catch
                {
                    // Preserve the primary bind/upload exception. The failed
                    // key contains no secret and remains visible in provider
                    // inventory for operator reconciliation.
                }
            }
        }
    }

    private async Task<string?> CopyTemplateObjectAsync(
        string? sourceKey,
        string destinationPrefix,
        ICollection<string> copiedKeys,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(sourceKey))
            return null;

        var extension = Path.GetExtension(sourceKey);
        if (extension.Length > 20 || extension.Any(character => !char.IsAsciiLetterOrDigit(character) && character != '.'))
            extension = string.Empty;
        var destinationKey = $"{destinationPrefix}/{Guid.NewGuid():N}{extension}";
        await using var source = await storageProvider.DownloadAsync(sourceKey, ct);
        var copiedKey = await storageProvider.UploadAsync(
            destinationKey,
            source,
            "application/octet-stream",
            ct);
        copiedKeys.Add(copiedKey);
        return copiedKey;
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
    ICtfScoreRebuilder ctfScoreRebuilder,
    IRedisLeaderboardCache leaderboardCache,
    IStorageProvider storageProvider,
    ICompetitionNotificationOutbox notificationOutbox)
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
            .FirstOrDefaultAsync(c =>
                c.Id == challengeId &&
                c.CompetitionId == competitionId &&
                !c.IsDeleting,
                ct);

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
        var existingHintContents = existingHints
            .Select(item => item.Content.Trim())
            .ToHashSet(StringComparer.Ordinal);
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

        var competitionRunning = await db.Competitions.IgnoreQueryFilters().AsNoTracking()
            .AnyAsync(item => item.Id == competitionId && item.Status == CompetitionStatus.Running, ct);
        if (competitionRunning)
        {
            foreach (var hint in hints.Where(item => !existingHintContents.Contains(item.Content)))
            {
                var digest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(hint.Content))).ToLowerInvariant();
                notificationOutbox.Add(CompetitionNotification.Create(
                    competitionId, CompetitionNotificationTypes.HintPublished,
                    "challenge", challenge.Id, null,
                    $"hint.published:{challenge.Id:N}:{digest}",
                    new
                    {
                        problem_title = challenge.Title,
                        hint_title = $"提示 {hint.DisplayOrder}",
                        hint_content = hint.Content,
                        occurred_at = hint.CreatedAt.ToString("O")
                    }));
            }
        }

        await db.SaveChangesAsync(ct);
        await ctfScoreRebuilder.RebuildChallengeAsync(competitionId, challengeId, ct);
        await leaderboardCache.InvalidateAsync(competitionId, ct);
        var response = ChallengeAdminMapping.ToCompetitionDto(challenge, hints);
        await StorageUrlResolver.ResolveAsync(response, storageProvider, challenge, ct);
        await SendAsync(response, cancellation: ct);
    }
}

public class DeleteCompetitionChallengeEndpoint(
    ApplicationDbContext db,
    ICompetitionPermissionService permissions,
    ILeaderboardService leaderboardService,
    IRedisLeaderboardCache leaderboardCache,
    IHubNotifierService hubNotifier,
    IContainerManager containerManager,
    ICompetitionExecutionLease executionLease)
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

        await using (var preparationLease = await executionLease.TryAcquireAsync(
            db,
            CompetitionExecutionLeaseKeys.RuntimePreparation,
            competitionId,
            ct))
        {
            if (preparationLease is null)
            {
                await SendStringAsync("runtime_preparation_in_progress", 409, cancellation: ct);
                return;
            }

            using var preparationCts = CancellationTokenSource.CreateLinkedTokenSource(
                ct,
                preparationLease.LostToken);
            challenge.IsDeleting = true;
            await db.SaveChangesAsync(preparationCts.Token);
        }

        // Hide the challenge's score contribution before potentially slow
        // runtime and object cleanup starts. The cache also has a finite TTL,
        // so a transient final refresh failure cannot leave a permanent view.
        await leaderboardCache.InvalidateAsync(competitionId, ct);

        var patchArchiveKeys = await db.AwdpPatchSubmissions
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(submission =>
                submission.CompetitionId == competitionId &&
                submission.ChallengeId == challengeId)
            .Select(submission => submission.PatchArchiveUrl)
            .ToListAsync(ct);

        var storageKeys = new[] { challenge.AttachmentStorageKey, challenge.PatchTemplateStorageKey }
            .Concat(patchArchiveKeys);
        await CleanupChallengeRuntimeAsync(
            db,
            containerManager,
            executionLease,
            challenge,
            HttpContext,
            userId,
            ct);

        // Runtime destruction can be slow and must not hold a database
        // transaction. Once it has succeeded, remove every database reference
        // and enqueue the corresponding object cleanup in one transaction so a
        // process crash cannot strand uploaded archives.
        await using var cleanupTransaction = db.Database.IsRelational()
            ? await db.Database.BeginTransactionAsync(ct)
            : null;
        await DeleteChallengeDataAsync(db, challenge, ct);
        db.Challenges.Remove(challenge);
        await StorageObjectCleanup.EnqueueAsync(db, storageKeys, ct);
        await db.SaveChangesAsync(ct);
        if (cleanupTransaction is not null)
            await cleanupTransaction.CommitAsync(ct);
        await RefreshLeaderboardAsync(competitionId, ct);
        await SendNoContentAsync(ct);
    }

    public static async Task DeleteChallengeArtifactsAsync(
        ApplicationDbContext db,
        IContainerManager containerManager,
        ICompetitionExecutionLease executionLease,
        Challenge challenge,
        HttpContext? httpContext,
        Guid? userId,
        CancellationToken ct)
    {
        await CleanupChallengeRuntimeAsync(
            db,
            containerManager,
            executionLease,
            challenge,
            httpContext,
            userId,
            ct);
        await DeleteChallengeDataAsync(db, challenge, ct);
    }

    private static async Task CleanupChallengeRuntimeAsync(
        ApplicationDbContext db,
        IContainerManager containerManager,
        ICompetitionExecutionLease executionLease,
        Challenge challenge,
        HttpContext? httpContext,
        Guid? userId,
        CancellationToken ct)
    {
        var competitionId = challenge.CompetitionId;
        var challengeId = challenge.Id;

        var runtimeTeamIds = await db.AwdGameBoxes
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(box => box.CompetitionId == competitionId && box.ChallengeId == challengeId)
            .Select(box => box.TeamId)
            .Concat(db.TeamChallengeInstances.IgnoreQueryFilters().AsNoTracking()
                .Where(instance => instance.CompetitionId == competitionId && instance.ChallengeId == challengeId)
                .Select(instance => instance.TeamId))
            .Distinct()
            .ToListAsync(ct);
        var boxes = new List<AwdGameBox>();
        var teamInstances = new List<TeamChallengeInstance>();
        foreach (var runtimeTeamId in runtimeTeamIds)
        {
            await using var lease = await executionLease.TryAcquireAsync(
                db,
                CompetitionExecutionLeaseKeys.ChallengeInstance(runtimeTeamId, challengeId),
                competitionId,
                ct) ?? throw new InvalidOperationException("instance_transition_in_progress");
            using var leaseCts = CancellationTokenSource.CreateLinkedTokenSource(ct, lease.LostToken);

            var box = await db.AwdGameBoxes
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(g =>
                    g.CompetitionId == competitionId &&
                    g.TeamId == runtimeTeamId &&
                    g.ChallengeId == challengeId,
                    leaseCts.Token);
            if (box is not null)
            {
                boxes.Add(box);
                if (!string.IsNullOrWhiteSpace(box.ContainerInstanceId) ||
                    !string.IsNullOrWhiteSpace(box.ComposeProjectName))
                {
                    await CreateChallengeInstanceEndpoint.DestroyTrackedBoxAsync(
                        db,
                        containerManager,
                        box,
                        httpContext,
                        "challenge_deleted",
                        userId,
                        updateCooldown: false,
                        leaseCts.Token);
                }
            }

            var instance = await db.TeamChallengeInstances
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(i =>
                    i.CompetitionId == competitionId &&
                    i.TeamId == runtimeTeamId &&
                    i.ChallengeId == challengeId,
                    leaseCts.Token);
            if (instance is not null)
            {
                teamInstances.Add(instance);
                if (!string.IsNullOrWhiteSpace(instance.ComposeProjectName))
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
                        ExpectedStopAt: instance.ExpiresAt), leaseCts.Token);
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
            }
        }
        db.AwdGameBoxes.RemoveRange(boxes);
        db.TeamChallengeInstances.RemoveRange(teamInstances);

        await ContainerCleanupRuntime.CleanupDetachedAwdpContainersAsync(
            db,
            containerManager,
            competitionId,
            teamId: null,
            challengeId: challengeId,
            ct: ct);
    }

    private static async Task DeleteChallengeDataAsync(
        ApplicationDbContext db,
        Challenge challenge,
        CancellationToken ct)
    {
        var competitionId = challenge.CompetitionId;
        var challengeId = challenge.Id;

        await DeleteCompetitionEndpoint.DeleteAsync(db, db.ChallengeHints
            .IgnoreQueryFilters()
            .Where(h => h.CompetitionId == competitionId && h.ChallengeId == challengeId), ct);

        var penetrationTopologyIds = await db.PenetrationTopologies
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(t => t.CompetitionId == competitionId && t.ChallengeId == challengeId)
            .Select(t => t.Id)
            .ToListAsync(ct);

        var penetrationFlagIds = await db.PenetrationFlags
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(f =>
                f.CompetitionId == competitionId &&
                (f.ChallengeId == challengeId || penetrationTopologyIds.Contains(f.TopologyId)))
            .Select(f => f.Id)
            .ToListAsync(ct);

        await DeleteCompetitionEndpoint.DeleteAsync(db, db.Submissions.IgnoreQueryFilters()
            .Where(s => s.CompetitionId == competitionId && s.ChallengeId == challengeId), ct);
        await DeleteCompetitionEndpoint.DeleteAsync(db, db.ScoreEvents.IgnoreQueryFilters()
            .Where(s => s.CompetitionId == competitionId && s.ChallengeId == challengeId), ct);
        await DeleteCompetitionEndpoint.DeleteAsync(db, db.ScoreSignals.IgnoreQueryFilters()
            .Where(s =>
                s.CompetitionId == competitionId &&
                (s.SubjectId == challengeId ||
                 (s.SubjectId.HasValue && penetrationFlagIds.Contains(s.SubjectId.Value)))), ct);
        await DeleteCompetitionEndpoint.DeleteAsync(db, db.CtfDynamicFlags.IgnoreQueryFilters()
            .Where(f => f.CompetitionId == competitionId && f.ChallengeId == challengeId), ct);
        await DeleteCompetitionEndpoint.DeleteAsync(db, db.DynamicFlagInstances.IgnoreQueryFilters()
            .Where(f => f.CompetitionId == competitionId && f.ChallengeId == challengeId), ct);
        await DeleteCompetitionEndpoint.DeleteAsync(db, db.CheatIncidents.IgnoreQueryFilters()
            .Where(i => i.CompetitionId == competitionId && i.ChallengeId == challengeId), ct);
        await DeleteCompetitionEndpoint.DeleteAsync(db, db.AwdFlags.IgnoreQueryFilters()
            .Where(f => f.CompetitionId == competitionId && f.ChallengeId == challengeId), ct);
        await DeleteCompetitionEndpoint.DeleteAsync(db, db.AwdAttackRecords.IgnoreQueryFilters()
            .Where(r => r.CompetitionId == competitionId && r.ChallengeId == challengeId), ct);
        await DeleteCompetitionEndpoint.DeleteAsync(db, db.AwdCheckResults.IgnoreQueryFilters()
            .Where(r => r.CompetitionId == competitionId && r.ChallengeId == challengeId), ct);
        await DeleteCompetitionEndpoint.DeleteAsync(db, db.AwdpTeamChallengeStates.IgnoreQueryFilters()
            .Where(s => s.CompetitionId == competitionId && s.ChallengeId == challengeId), ct);
        await DeleteCompetitionEndpoint.DeleteAsync(db, db.AwdpRoundScores.IgnoreQueryFilters()
            .Where(s => s.CompetitionId == competitionId && s.ChallengeId == challengeId), ct);

        var awdpPatchSubmissionIds = await db.AwdpPatchSubmissions
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(s => s.CompetitionId == competitionId && s.ChallengeId == challengeId)
            .Select(s => s.Id)
            .ToListAsync(ct);
        await RemoveAwdpPatchTasksAsync(db, competitionId, challengeId, awdpPatchSubmissionIds, ct);
        await DeleteCompetitionEndpoint.DeleteAsync(db, db.AwdpPatchSubmissions.IgnoreQueryFilters()
            .Where(s => s.CompetitionId == competitionId && s.ChallengeId == challengeId), ct);

        await DeleteCompetitionEndpoint.DeleteAsync(db, db.PenetrationNodes.IgnoreQueryFilters()
            .Where(n => n.CompetitionId == competitionId && penetrationTopologyIds.Contains(n.TopologyId)), ct);
        await DeleteCompetitionEndpoint.DeleteAsync(db, db.PenetrationFlags.IgnoreQueryFilters()
            .Where(f => f.CompetitionId == competitionId &&
                (f.ChallengeId == challengeId || penetrationTopologyIds.Contains(f.TopologyId))), ct);
        await DeleteCompetitionEndpoint.DeleteAsync(db, db.PenetrationTopologies.IgnoreQueryFilters()
            .Where(t => t.CompetitionId == competitionId && t.ChallengeId == challengeId), ct);
        await DeleteCompetitionEndpoint.DeleteAsync(db, db.KohControlRecords.IgnoreQueryFilters()
            .Where(r => r.CompetitionId == competitionId && r.ChallengeId == challengeId), ct);
    }

    private async Task RefreshLeaderboardAsync(Guid competitionId, CancellationToken ct)
    {
        var cacheVersion = await leaderboardCache.ReserveUpdateVersionAsync(competitionId, ct);
        var entries = await leaderboardService.CalculateLeaderboardAsync(competitionId, ct);
        await leaderboardCache.UpdateAsync(competitionId, entries, cacheVersion, ct);
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

    private static async Task RemoveAwdpPatchTasksAsync(
        ApplicationDbContext db,
        Guid competitionId,
        Guid challengeId,
        IReadOnlyCollection<Guid> submissionIds,
        CancellationToken ct)
    {
        if (submissionIds.Count > 0)
        {
            var submissionIdText = submissionIds.Select(id => id.ToString()).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var validationTasks = await db.BackgroundTasks
                .IgnoreQueryFilters()
                .Where(t => t.CompetitionId == competitionId && t.Type == "awdp.patch.validation")
                .ToListAsync(ct);
            db.BackgroundTasks.RemoveRange(validationTasks.Where(t =>
                submissionIdText.Any(id => t.PayloadJson.Contains(id, StringComparison.OrdinalIgnoreCase))));
        }

        var challengeIdText = challengeId.ToString();
        await DeleteCompetitionEndpoint.DeleteAsync(db, db.BackgroundTasks
            .IgnoreQueryFilters()
            .Where(t =>
                t.CompetitionId == competitionId &&
                t.Type == "awdp.container.cleanup" &&
                t.PayloadJson.Contains(challengeIdText)), ct);
    }
}
