using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using NoCTF.Core;
using NoCTF.Infrastructure;
using NoCTF.PluginBase;
using NoCTF.Application.BackgroundTasks;

namespace NoCTF.Plugins.Penetration;

public class PenetrationInstanceService(
    ApplicationDbContext db,
    IContainerManager containerManager,
    IConfiguration configuration,
    PenetrationComposeBuilder composeBuilder,
    PenetrationFlagService flagService,
    ICompetitionExecutionLease? executionLease = null)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly PenetrationInstanceStatus[] BusyStatuses =
    [
        PenetrationInstanceStatus.Starting,
        PenetrationInstanceStatus.Stopping,
        PenetrationInstanceStatus.Resetting,
        PenetrationInstanceStatus.Destroying
    ];

    public async Task<PenetrationChallengeDetailDto> GetDetailAsync(
        Guid competitionId,
        Guid challengeId,
        Guid teamId,
        CancellationToken ct)
    {
        await EnsureCompetitionAllowsPlayerViewAsync(competitionId, ct);
        var challenge = await LoadChallengeAsync(competitionId, challengeId, ct);
        var topology = await db.PenetrationTopologies
            .IgnoreQueryFilters()
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.CompetitionId == competitionId && t.ChallengeId == challengeId, ct);
        var topologyDto = topology is null
            ? null
            : await BuildTopologyDtoWithSolvesAsync(competitionId, challengeId, teamId, topology.Id, ct);
        var instance = await LoadInstanceAsync(competitionId, challengeId, teamId, ct);
        var config = PenetrationTopologyService.ReadConfig(challenge);
        var flags = topologyDto?.Flags.Where(f => f.Visible).ToList() ?? [];

        return new PenetrationChallengeDetailDto
        {
            CompetitionId = competitionId,
            ChallengeId = challengeId,
            AuthorizationScope = "Only your team's dedicated target range and entry service are in scope.",
            Topology = topologyDto,
            Instance = ToDto(instance, config),
            TotalStageCount = flags.Count,
            SolvedStageCount = flags.Count(f => f.Solved),
            TotalScore = flags.Sum(f => f.Score),
        };
    }

    public async Task<PenetrationInstanceDto> GetInstanceAsync(
        Guid competitionId,
        Guid challengeId,
        Guid teamId,
        CancellationToken ct)
    {
        await EnsureCompetitionAllowsPlayerViewAsync(competitionId, ct);
        var challenge = await LoadChallengeAsync(competitionId, challengeId, ct);
        var instance = await LoadInstanceAsync(competitionId, challengeId, teamId, ct);
        return ToDto(instance, PenetrationTopologyService.ReadConfig(challenge));
    }

    public async Task<PenetrationInstanceDto> StartAsync(
        Guid competitionId,
        Guid challengeId,
        Guid teamId,
        Guid userId,
        CancellationToken ct)
    {
        await using var transitionLease = await AcquireTransitionLeaseAsync(
            competitionId, challengeId, teamId, ct);
        using var leaseCts = CancellationTokenSource.CreateLinkedTokenSource(ct, transitionLease.LostToken);
        return await StartCoreAsync(competitionId, challengeId, teamId, userId, leaseCts.Token);
    }

    private async Task<PenetrationInstanceDto> StartCoreAsync(
        Guid competitionId,
        Guid challengeId,
        Guid teamId,
        Guid userId,
        CancellationToken ct)
    {
        var challenge = await LoadChallengeAsync(competitionId, challengeId, ct);
        var config = PenetrationTopologyService.ReadConfig(challenge);
        await EnsureCompetitionAllowsInstanceActionsAsync(competitionId, ct);

        var topology = await LoadTopologyAsync(competitionId, challengeId, ct);
        var nodes = await LoadNodesAsync(competitionId, topology.Id, ct);
        var flags = await LoadFlagsAsync(competitionId, challengeId, ct);
        var now = DateTime.UtcNow;
        var instance = await LoadInstanceAsync(competitionId, challengeId, teamId, ct);

        if (IsRunning(instance, now))
            return ToDto(instance, config);
        if (instance is not null && BusyStatuses.Contains(instance.Status))
            throw new InvalidOperationException("instance_busy");
        if (IsCoolingDown(instance, config, now))
            throw new InvalidOperationException("instance_cooldown");
        if (instance is not null &&
            instance.ComposeProjectName is not null &&
            instance.Status is not (
                PenetrationInstanceStatus.Stopped or
                PenetrationInstanceStatus.Expired or
                PenetrationInstanceStatus.Destroyed))
        {
            await DownBestEffortAsync(instance, ct);
        }

        var leaseService = executionLease ?? new CompetitionExecutionLease();
        await using (var preparationLease = await leaseService.TryAcquireAsync(
            db,
            CompetitionExecutionLeaseKeys.RuntimePreparation,
            competitionId,
            ct))
        {
            if (preparationLease is null)
                throw new InvalidOperationException("runtime_preparation_in_progress");

            using var preparationCts = CancellationTokenSource.CreateLinkedTokenSource(
                ct,
                preparationLease.LostToken);
            var preparationCt = preparationCts.Token;

            // Re-read deletion/admission state under the short preparation
            // barrier before making the runtime operation durable.
            challenge = await LoadChallengeAsync(competitionId, challengeId, preparationCt);
            config = PenetrationTopologyService.ReadConfig(challenge);
            await EnsureCompetitionAllowsInstanceActionsAsync(competitionId, preparationCt);
            await EnsureTeamAllowsInstanceActionsAsync(competitionId, teamId, preparationCt);

            var reuseFailedOperation = instance?.Status == PenetrationInstanceStatus.Failed &&
                                       instance.RuntimeOperationId.HasValue;
            instance ??= new TeamChallengeInstance
            {
                Id = Guid.NewGuid(),
                CompetitionId = competitionId,
                TeamId = teamId,
                ChallengeId = challengeId,
                TopologyId = topology.Id,
                CreatedAt = now,
            };

            instance.Status = PenetrationInstanceStatus.Starting;
            if (!reuseFailedOperation)
                instance.RuntimeOperationId = Guid.NewGuid();
            instance.TopologyId = topology.Id;
            instance.ComposeProjectName = BuildProjectName(competitionId, challengeId, teamId);
            instance.LastActionAt = now;
            instance.UpdatedAt = now;
            instance.LastError = null;
            if (db.Entry(instance).State == EntityState.Detached)
                db.TeamChallengeInstances.Add(instance);
            await db.SaveChangesAsync(preparationCt);
        }

        try
        {
            var dynamicFlags = await flagService.RegenerateDynamicFlagsAsync(challenge, instance, flags, ct);
            var build = composeBuilder.Build(challenge, instance, nodes, flags, dynamicFlags);
            instance.RenderedComposeYaml = build.RedactedComposeYaml;
            instance.UpdatedAt = DateTime.UtcNow;

            // Persist cleanup identity before dispatching ComposeUp. If the
            // process dies after Runner accepts the operation, maintenance can
            // now deterministically tear down the project instead of leaking it
            // because the durable row still had an empty compose document.
            await db.SaveChangesAsync(ct);

            await containerManager.ComposeUpAsync(new ComposeConfig(
                ProjectName: instance.ComposeProjectName!,
                ComposeYaml: build.ComposeYaml,
                Labels: BuildLabels(challenge, instance),
                OrchestrationJson: challenge.OrchestrationJson,
                Ttl: TimeSpan.FromSeconds(config.InstanceTtlSeconds),
                OperationId: instance.RuntimeOperationId), ct);

            var composeStatus = await containerManager.GetComposeStatusAsync(
                instance.ComposeProjectName!,
                BuildLabels(challenge, instance),
                ct);
            var entryService = composeStatus.Services.FirstOrDefault(s => s.NodeId == build.EntryNode.Id);
            var hostPort = entryService?.PublishedPorts.GetValueOrDefault(build.EntryContainerPort) ?? 0;
            var containerIds = composeStatus.Services.Select(s => s.ContainerId).Where(id => !string.IsNullOrWhiteSpace(id)).ToArray();

            instance.Status = PenetrationInstanceStatus.Running;
            instance.EntryHost = !string.IsNullOrWhiteSpace(entryService?.PublicHost)
                ? entryService.PublicHost
                : ResolveAccessHost();
            instance.EntryPort = hostPort > 0 ? hostPort : null;
            instance.EntryUrl = !string.IsNullOrWhiteSpace(entryService?.EntryUrl)
                ? entryService.EntryUrl
                : BuildEntryUrl(instance.EntryHost, instance.EntryPort, topology.EntryConfigJson);
            instance.ContainerIdsJson = JsonSerializer.Serialize(containerIds, JsonOptions);
            instance.PortMappingsJson = hostPort > 0
                ? JsonSerializer.Serialize(new Dictionary<int, int> { [build.EntryContainerPort] = hostPort }, JsonOptions)
                : "{}";
            instance.ExpiresAt = now.AddSeconds(config.InstanceTtlSeconds);
            instance.UpdatedAt = DateTime.UtcNow;

            AddCompetitionLog(
                db,
                competitionId,
                "penetration.instance.started",
                "Penetration range instance was started.",
                teamId: teamId,
                userId: userId,
                challengeId: challengeId,
                metadata: new
                {
                    instanceId = instance.Id,
                    projectName = instance.ComposeProjectName,
                    entryPort = instance.EntryPort,
                    expiresAt = instance.ExpiresAt
                });
            await db.SaveChangesAsync(ct);
            return ToDto(instance, config);
        }
        catch (Exception)
        {
            var cleanupSucceeded = false;
            try
            {
                await DownBestEffortAsync(instance, ct);
                cleanupSucceeded = true;
            }
            catch (Exception)
            {
                instance.LastError = "instance_start_and_cleanup_failed";
            }
            await DeactivateDynamicFlagsAsync(instance, ct);
            instance.Status = PenetrationInstanceStatus.Failed;
            if (cleanupSucceeded)
                instance.RuntimeOperationId = null;
            instance.LastError ??= "instance_start_failed";
            instance.UpdatedAt = DateTime.UtcNow;
            AddCompetitionLog(
                db,
                competitionId,
                "penetration.instance.start_failed",
                "Penetration range instance failed to start.",
                "error",
                teamId,
                userId,
                challengeId,
                metadata: new { instanceId = instance.Id, error = "instance_start_failed" });
            await db.SaveChangesAsync(ct);
            throw;
        }
    }

    public async Task<PenetrationInstanceDto> StopAsync(
        Guid competitionId,
        Guid challengeId,
        Guid teamId,
        Guid userId,
        CancellationToken ct)
    {
        await using var transitionLease = await AcquireTransitionLeaseAsync(
            competitionId, challengeId, teamId, ct);
        using var leaseCts = CancellationTokenSource.CreateLinkedTokenSource(ct, transitionLease.LostToken);
        var leaseCt = leaseCts.Token;
        var challenge = await LoadChallengeAsync(competitionId, challengeId, leaseCt);
        var instance = await LoadInstanceAsync(competitionId, challengeId, teamId, leaseCt);
        var config = PenetrationTopologyService.ReadConfig(challenge);
        if (instance is null) return ToDto(null, config);
        if (BusyStatuses.Contains(instance.Status)) throw new InvalidOperationException("instance_busy");

        instance.Status = PenetrationInstanceStatus.Stopping;
        instance.LastActionAt = DateTime.UtcNow;
        await db.SaveChangesAsync(leaseCt);
        try
        {
            await DownBestEffortAsync(instance, leaseCt);
        }
        catch (Exception ex)
        {
            await MarkOperationFailedAsync(instance, ex, leaseCt);
            throw;
        }
        await DeactivateDynamicFlagsAsync(instance, leaseCt);
        instance.Status = PenetrationInstanceStatus.Stopped;
        instance.RuntimeOperationId = null;
        instance.EntryPort = null;
        instance.EntryUrl = null;
        instance.ContainerIdsJson = "[]";
        instance.PortMappingsJson = "{}";
        instance.UpdatedAt = DateTime.UtcNow;
        AddCompetitionLog(db, competitionId, "penetration.instance.stopped", "Penetration range instance was stopped.", teamId: teamId, userId: userId, challengeId: challengeId, metadata: new { instanceId = instance.Id });
        await db.SaveChangesAsync(leaseCt);
        return ToDto(instance, config);
    }

    public async Task<PenetrationInstanceDto> ResetAsync(
        Guid competitionId,
        Guid challengeId,
        Guid teamId,
        Guid userId,
        bool adminOverride,
        CancellationToken ct)
    {
        await using var transitionLease = await AcquireTransitionLeaseAsync(
            competitionId, challengeId, teamId, ct);
        using var leaseCts = CancellationTokenSource.CreateLinkedTokenSource(ct, transitionLease.LostToken);
        var leaseCt = leaseCts.Token;
        var challenge = await LoadChallengeAsync(competitionId, challengeId, leaseCt);
        var config = PenetrationTopologyService.ReadConfig(challenge);
        if (!adminOverride)
            await EnsureCompetitionAllowsInstanceActionsAsync(competitionId, leaseCt);
        if (!config.AllowReset && !adminOverride)
            throw new InvalidOperationException("reset_not_allowed");

        var instance = await LoadInstanceAsync(competitionId, challengeId, teamId, leaseCt)
            ?? throw new InvalidOperationException("instance_not_found");
        if (BusyStatuses.Contains(instance.Status)) throw new InvalidOperationException("instance_busy");
        if (!adminOverride && instance.ResetCount >= config.MaxResetCount)
            throw new InvalidOperationException("reset_limit_exceeded");
        if (!adminOverride && IsCoolingDown(instance, config, DateTime.UtcNow))
            throw new InvalidOperationException("instance_cooldown");

        instance.Status = PenetrationInstanceStatus.Resetting;
        instance.LastActionAt = DateTime.UtcNow;
        instance.ResetCount += 1;
        await db.SaveChangesAsync(leaseCt);
        try
        {
            await DownBestEffortAsync(instance, leaseCt);
        }
        catch (Exception ex)
        {
            await MarkOperationFailedAsync(instance, ex, leaseCt);
            throw;
        }
        instance.Status = PenetrationInstanceStatus.Stopped;
        instance.RuntimeOperationId = null;
        instance.LastActionAt = null;
        await db.SaveChangesAsync(leaseCt);
        AddCompetitionLog(db, competitionId, adminOverride ? "penetration.admin.instance_reset" : "penetration.instance.reset", "Penetration range instance was reset.", teamId: teamId, userId: userId, challengeId: challengeId, metadata: new { instanceId = instance.Id, instance.ResetCount });
        await db.SaveChangesAsync(leaseCt);
        return await StartCoreAsync(competitionId, challengeId, teamId, userId, leaseCt);
    }

    public async Task<PenetrationInstanceDto> DestroyAsync(
        Guid competitionId,
        Guid challengeId,
        Guid teamId,
        Guid userId,
        bool adminOverride,
        CancellationToken ct)
    {
        await using var transitionLease = await AcquireTransitionLeaseAsync(
            competitionId, challengeId, teamId, ct);
        using var leaseCts = CancellationTokenSource.CreateLinkedTokenSource(ct, transitionLease.LostToken);
        var leaseCt = leaseCts.Token;
        var challenge = await LoadChallengeAsync(competitionId, challengeId, leaseCt);
        var config = PenetrationTopologyService.ReadConfig(challenge);
        var instance = await LoadInstanceAsync(competitionId, challengeId, teamId, leaseCt);
        if (instance is null) return ToDto(null, config);
        if (BusyStatuses.Contains(instance.Status)) throw new InvalidOperationException("instance_busy");

        instance.Status = PenetrationInstanceStatus.Destroying;
        instance.LastActionAt = DateTime.UtcNow;
        await db.SaveChangesAsync(leaseCt);
        try
        {
            await DownBestEffortAsync(instance, leaseCt);
        }
        catch (Exception ex)
        {
            await MarkOperationFailedAsync(instance, ex, leaseCt);
            throw;
        }
        await DeactivateDynamicFlagsAsync(instance, leaseCt);

        instance.Status = PenetrationInstanceStatus.Destroyed;
        instance.RuntimeOperationId = null;
        instance.EntryPort = null;
        instance.EntryUrl = null;
        instance.ContainerIdsJson = "[]";
        instance.PortMappingsJson = "{}";
        instance.UpdatedAt = DateTime.UtcNow;
        AddCompetitionLog(db, competitionId, adminOverride ? "penetration.admin.instance_destroy" : "penetration.instance.destroyed", "Penetration range instance was destroyed.", teamId: teamId, userId: userId, challengeId: challengeId, metadata: new { instanceId = instance.Id });
        await db.SaveChangesAsync(leaseCt);
        return ToDto(instance, config);
    }

    public async Task<object> ListAdminInstancesAsync(Guid competitionId, Guid? challengeId, Guid? teamId, CancellationToken ct)
    {
        var query = db.TeamChallengeInstances.IgnoreQueryFilters().AsNoTracking().Where(i => i.CompetitionId == competitionId);
        if (challengeId.HasValue) query = query.Where(i => i.ChallengeId == challengeId.Value);
        if (teamId.HasValue) query = query.Where(i => i.TeamId == teamId.Value);

        var rows = await query
            .Join(db.Teams.IgnoreQueryFilters().AsNoTracking().Where(t => t.CompetitionId == competitionId),
                i => i.TeamId,
                t => t.Id,
                (i, t) => new { Instance = i, TeamName = t.Name })
            .Join(db.Challenges.IgnoreQueryFilters().AsNoTracking().Where(c =>
                    c.CompetitionId == competitionId &&
                    !c.IsDeleting),
                row => row.Instance.ChallengeId,
                c => c.Id,
                (row, c) => new
                {
                    id = row.Instance.Id,
                    row.Instance.TeamId,
                    row.TeamName,
                    row.Instance.ChallengeId,
                    challengeTitle = c.Title,
                    status = row.Instance.Status.ToString(),
                    row.Instance.EntryUrl,
                    row.Instance.ResetCount,
                    row.Instance.ExpiresAt,
                    row.Instance.LastError,
                    row.Instance.CreatedAt,
                    row.Instance.UpdatedAt
                })
            .OrderBy(row => row.challengeTitle)
            .ThenBy(row => row.TeamName)
            .ToListAsync(ct);

        return new { items = rows };
    }

    public async Task<(TeamChallengeInstance Instance, Challenge Challenge)> LoadInstanceForAdminAsync(
        Guid competitionId,
        Guid instanceId,
        CancellationToken ct)
    {
        var instance = await db.TeamChallengeInstances
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(i => i.CompetitionId == competitionId && i.Id == instanceId, ct)
            ?? throw new InvalidOperationException("instance_not_found");
        var challenge = await LoadChallengeAsync(competitionId, instance.ChallengeId, ct);
        return (instance, challenge);
    }

    private async Task<PenetrationTopologyDto> BuildTopologyDtoWithSolvesAsync(
        Guid competitionId,
        Guid challengeId,
        Guid teamId,
        Guid topologyId,
        CancellationToken ct)
    {
        var topologyService = new PenetrationTopologyService(db);
        var dto = await topologyService.GetCompetitionTopologyAsync(competitionId, challengeId, ct);
        var solved = await db.Submissions
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(s =>
                s.CompetitionId == competitionId &&
                s.TeamId == teamId &&
                s.ChallengeId == challengeId &&
                s.IsCorrect &&
                s.PenetrationFlagId != null)
            .Select(s => new { FlagId = s.PenetrationFlagId!.Value, s.SubmittedAt })
            .ToListAsync(ct);
        var solvedAtByFlag = solved
            .GroupBy(submission => submission.FlagId)
            .ToDictionary(
                group => group.Key,
                group => group.Min(submission => submission.SubmittedAt));

        foreach (var flag in dto.Flags)
        {
            flag.Solved = solvedAtByFlag.TryGetValue(flag.Id, out var solvedAt);
            flag.SolvedAt = flag.Solved ? solvedAt : null;
        }

        return dto;
    }

    private async Task<Challenge> LoadChallengeAsync(Guid competitionId, Guid challengeId, CancellationToken ct)
    {
        var challenge = await db.Challenges
            .IgnoreQueryFilters()
            .AsNoTracking()
            .FirstOrDefaultAsync(c =>
                c.CompetitionId == competitionId &&
                c.Id == challengeId &&
                !c.IsDeleting,
                ct)
            ?? throw new InvalidOperationException("challenge_not_found");
        if (!string.Equals(challenge.TypeId, PenetrationConstants.TypeId, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("not_penetration_challenge");
        return challenge;
    }

    private async Task<PenetrationTopology> LoadTopologyAsync(Guid competitionId, Guid challengeId, CancellationToken ct)
        => await db.PenetrationTopologies
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(t => t.CompetitionId == competitionId && t.ChallengeId == challengeId, ct)
            ?? throw new InvalidOperationException("penetration_topology_not_configured");

    private Task<List<PenetrationNode>> LoadNodesAsync(Guid competitionId, Guid topologyId, CancellationToken ct)
        => db.PenetrationNodes
            .IgnoreQueryFilters()
            .Where(n => n.CompetitionId == competitionId && n.TopologyId == topologyId)
            .OrderBy(n => n.DisplayOrder)
            .ToListAsync(ct);

    private Task<List<PenetrationFlag>> LoadFlagsAsync(Guid competitionId, Guid challengeId, CancellationToken ct)
        => db.PenetrationFlags
            .IgnoreQueryFilters()
            .Where(f => f.CompetitionId == competitionId && f.ChallengeId == challengeId)
            .OrderBy(f => f.Stage)
            .ToListAsync(ct);

    private Task<TeamChallengeInstance?> LoadInstanceAsync(Guid competitionId, Guid challengeId, Guid teamId, CancellationToken ct)
        => db.TeamChallengeInstances
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(i => i.CompetitionId == competitionId && i.TeamId == teamId && i.ChallengeId == challengeId, ct);

    private async Task EnsureCompetitionAllowsInstanceActionsAsync(Guid competitionId, CancellationToken ct)
    {
        var competition = await db.Competitions.IgnoreQueryFilters().AsNoTracking().FirstOrDefaultAsync(c => c.Id == competitionId, ct)
            ?? throw new InvalidOperationException("competition_not_found");
        var now = DateTime.UtcNow;
        if (now < competition.StartTime || competition.Status == CompetitionStatus.Draft) throw new InvalidOperationException("competition_not_started");
        if (now > competition.EndTime || competition.Status == CompetitionStatus.Finished) throw new InvalidOperationException("competition_ended");
        if (competition.Status == CompetitionStatus.Paused) throw new InvalidOperationException("competition_paused");
    }

    private async Task EnsureTeamAllowsInstanceActionsAsync(
        Guid competitionId,
        Guid teamId,
        CancellationToken ct)
    {
        var team = await db.Teams
            .IgnoreQueryFilters()
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.CompetitionId == competitionId && t.Id == teamId, ct)
            ?? throw new InvalidOperationException("team_not_found");
        if (team.RegistrationStatus != TeamRegistrationStatus.Approved)
            throw new InvalidOperationException("team_not_approved");
        if (team.IsBanned)
            throw new InvalidOperationException("team_banned");
    }

    private async Task EnsureCompetitionAllowsPlayerViewAsync(Guid competitionId, CancellationToken ct)
    {
        var competition = await db.Competitions.IgnoreQueryFilters().AsNoTracking().FirstOrDefaultAsync(c => c.Id == competitionId, ct)
            ?? throw new InvalidOperationException("competition_not_found");
        var now = DateTime.UtcNow;
        if (now < competition.StartTime || competition.Status == CompetitionStatus.Draft) throw new InvalidOperationException("competition_not_started");
        if (competition.Status == CompetitionStatus.Paused) throw new InvalidOperationException("competition_paused");
    }

    private async Task DownBestEffortAsync(TeamChallengeInstance instance, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(instance.ComposeProjectName))
            return;
        try
        {
            await containerManager.ComposeDownAsync(new ComposeDeployment(
                Id: Guid.NewGuid(),
                CompetitionId: instance.CompetitionId,
                TeamId: instance.TeamId,
                ChallengeId: instance.ChallengeId,
                ProviderType: "docker-compose",
                ProjectName: instance.ComposeProjectName,
                ComposeYaml: string.IsNullOrWhiteSpace(instance.RenderedComposeYaml)
                    ? "services:\n  cleanup:\n    image: scratch\n"
                    : instance.RenderedComposeYaml,
                Status: instance.Status.ToString(),
                StartedAt: instance.CreatedAt), ct);
        }
        catch (Exception)
        {
            instance.LastError = "instance_cleanup_failed";
            instance.UpdatedAt = DateTime.UtcNow;
            throw;
        }
    }

    private async Task<IExecutionLease> AcquireTransitionLeaseAsync(
        Guid competitionId,
        Guid challengeId,
        Guid teamId,
        CancellationToken ct)
    {
        var leaseService = executionLease ?? new CompetitionExecutionLease();
        return await leaseService.TryAcquireAsync(
                   db,
                   CompetitionExecutionLeaseKeys.ChallengeInstance(teamId, challengeId),
                   competitionId,
                   ct)
               ?? throw new InvalidOperationException("instance_busy");
    }

    private async Task MarkOperationFailedAsync(TeamChallengeInstance instance, Exception ex, CancellationToken ct)
    {
        instance.Status = PenetrationInstanceStatus.Failed;
        instance.LastError = "instance_operation_failed";
        instance.LastActionAt = null;
        instance.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
    }

    private async Task DeactivateDynamicFlagsAsync(TeamChallengeInstance instance, CancellationToken ct)
    {
        var activeFlags = db.DynamicFlagInstances
            .IgnoreQueryFilters()
            .Where(f =>
                f.CompetitionId == instance.CompetitionId &&
                f.TeamId == instance.TeamId &&
                f.ChallengeId == instance.ChallengeId &&
                f.InstanceId == instance.Id &&
                f.IsActive);
        if (db.Database.IsRelational())
        {
            await activeFlags.ExecuteUpdateAsync(
                setters => setters.SetProperty(flag => flag.IsActive, false),
                ct);
            foreach (var entry in db.ChangeTracker.Entries<DynamicFlagInstance>().Where(entry =>
                         entry.Entity.InstanceId == instance.Id && entry.Entity.IsActive))
            {
                entry.Entity.IsActive = false;
            }
            return;
        }

        var flags = await activeFlags.ToListAsync(ct);
        foreach (var flag in flags)
            flag.IsActive = false;
    }

    private static bool IsRunning(TeamChallengeInstance? instance, DateTime now)
        => instance?.Status == PenetrationInstanceStatus.Running &&
           (!instance.ExpiresAt.HasValue || instance.ExpiresAt.Value > now);

    private static bool IsCoolingDown(TeamChallengeInstance? instance, PenetrationRuntimeConfig config, DateTime now)
        => instance?.LastActionAt is { } lastAction &&
           lastAction.AddSeconds(Math.Max(0, config.ActionCooldownSeconds)) > now;

    private static PenetrationInstanceDto ToDto(TeamChallengeInstance? instance, PenetrationRuntimeConfig config)
    {
        if (instance is null)
        {
            return new PenetrationInstanceDto
            {
                ResetLimit = config.MaxResetCount,
                ServerTime = DateTime.UtcNow,
            };
        }

        var runtimeVisible = IsRunning(instance, DateTime.UtcNow);
        return new PenetrationInstanceDto
        {
            Id = instance.Id,
            Status = instance.Status.ToString(),
            EntryHost = runtimeVisible ? instance.EntryHost : null,
            EntryPort = runtimeVisible ? instance.EntryPort : null,
            EntryUrl = runtimeVisible && config.VisibleEntryAfterStart ? instance.EntryUrl : null,
            ResetCount = instance.ResetCount,
            ResetLimit = config.MaxResetCount,
            ExpiresAt = instance.ExpiresAt,
            CooldownUntil = instance.LastActionAt?.AddSeconds(Math.Max(0, config.ActionCooldownSeconds)),
            ServerTime = DateTime.UtcNow,
            LastError = string.IsNullOrWhiteSpace(instance.LastError)
                ? null
                : "instance_operation_failed",
            ContainerIds = runtimeVisible ? ReadStringArray(instance.ContainerIdsJson) : [],
            Ports = runtimeVisible ? ReadPortMap(instance.PortMappingsJson) : [],
        };
    }

    private string? ResolveAccessHost()
    {
        var configured = configuration["InstanceAccess:PublicHost"];
        if (!string.IsNullOrWhiteSpace(configured)) return configured.Trim();
        if (Uri.TryCreate(configuration["App:PublicBaseUrl"], UriKind.Absolute, out var appBaseUrl) &&
            !string.IsNullOrWhiteSpace(appBaseUrl.Host))
            return appBaseUrl.Host;

        return string.Equals(configuration["ASPNETCORE_ENVIRONMENT"], "Development", StringComparison.OrdinalIgnoreCase)
            ? "127.0.0.1"
            : null;
    }

    private static string? BuildEntryUrl(string? host, int? port, string entryConfigJson)
    {
        if (string.IsNullOrWhiteSpace(host) || port is null or <= 0) return null;
        var scheme = ReadEntryScheme(entryConfigJson);
        var formattedHost = host.Contains(':', StringComparison.Ordinal) && !host.StartsWith('[') ? $"[{host}]" : host;
        return scheme is "http" or "https"
            ? $"{scheme}://{formattedHost}:{port}"
            : $"{formattedHost}:{port}";
    }

    private static string ReadEntryScheme(string entryConfigJson)
    {
        try
        {
            using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(entryConfigJson) ? "{}" : entryConfigJson);
            return doc.RootElement.TryGetProperty("scheme", out var scheme) && scheme.ValueKind == JsonValueKind.String
                ? scheme.GetString()?.Trim().ToLowerInvariant() ?? "tcp"
                : "tcp";
        }
        catch
        {
            return "tcp";
        }
    }

    private static Dictionary<string, string> BuildLabels(Challenge challenge, TeamChallengeInstance instance)
        => new()
        {
            ["noctf.kind"] = "penetration",
            ["competitionId"] = challenge.CompetitionId.ToString(),
            ["challengeId"] = challenge.Id.ToString(),
            ["teamId"] = instance.TeamId.ToString(),
            ["instanceId"] = instance.Id.ToString(),
        };

    private static void AddCompetitionLog(
        ApplicationDbContext db,
        Guid competitionId,
        string eventType,
        string message,
        string level = "info",
        Guid? teamId = null,
        Guid? userId = null,
        Guid? challengeId = null,
        object? metadata = null)
    {
        db.CompetitionLogs.Add(new CompetitionLog
        {
            Id = Guid.NewGuid(),
            CompetitionId = competitionId,
            Level = string.IsNullOrWhiteSpace(level) ? "info" : level.Trim().ToLowerInvariant(),
            EventType = eventType,
            Message = message,
            TeamId = teamId,
            UserId = userId,
            ChallengeId = challengeId,
            MetadataJson = metadata is null ? "{}" : JsonSerializer.Serialize(metadata, JsonOptions),
            CreatedAt = DateTime.UtcNow,
        });
    }

    private static string BuildProjectName(Guid competitionId, Guid challengeId, Guid teamId)
        => $"noctf-pen-{ShortId(competitionId)}-{ShortId(challengeId)}-{ShortId(teamId)}";

    private static string ShortId(Guid id)
        => id.ToString("N")[..8];

    private static string[] ReadStringArray(string json)
    {
        try { return JsonSerializer.Deserialize<string[]>(json, JsonOptions) ?? []; }
        catch { return []; }
    }

    private static Dictionary<int, int> ReadPortMap(string json)
    {
        try { return JsonSerializer.Deserialize<Dictionary<int, int>>(json, JsonOptions) ?? []; }
        catch { return []; }
    }
}
