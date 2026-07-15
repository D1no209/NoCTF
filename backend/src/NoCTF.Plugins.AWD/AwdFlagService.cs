using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using NoCTF.Core;
using NoCTF.Infrastructure;
using NoCTF.PluginBase;
using System.Text.Json;
using NoCTF.Application.BackgroundTasks;

namespace NoCTF.Plugins.AWD;

/// <summary>
/// Generates deterministic per-round flags for AWD competitions and injects them
/// into running containers by recreating the container with the NOCTF_FLAG env var.
/// </summary>
public class AwdFlagService
{
    private readonly ApplicationDbContext _db;
    private readonly IContainerManager _containerManager;
    private readonly AwdChallengeRuntimeConfigProvider _runtimeConfigProvider;
    private readonly ILogger<AwdFlagService> _logger;
    private readonly ICompetitionExecutionLease _executionLease;
    private readonly int _maxConcurrentRefreshes;

    public AwdFlagService(
        ApplicationDbContext db,
        IContainerManager containerManager,
        AwdChallengeRuntimeConfigProvider runtimeConfigProvider,
        ILogger<AwdFlagService> logger,
        IConfiguration? configuration = null,
        ICompetitionExecutionLease? executionLease = null)
    {
        _db = db;
        _containerManager = containerManager;
        _runtimeConfigProvider = runtimeConfigProvider;
        _logger = logger;
        _executionLease = executionLease ?? new CompetitionExecutionLease();
        _maxConcurrentRefreshes = Math.Clamp(
            configuration?.GetValue("Awd:MaxConcurrentContainerRefreshes", 4) ?? 4,
            1,
            32);
    }

    /// <summary>
    /// Idempotently generates flags for all (team, challenge, round) combinations
    /// for the given competition. Already-existing flags are skipped.
    /// </summary>
    public Task GenerateFlagsAsync(Guid competitionId, int totalRounds, CancellationToken ct = default)
        => GenerateFlagsForRoundsAsync(
            competitionId,
            Enumerable.Range(1, Math.Max(0, totalRounds)).ToArray(),
            ct);

    /// <summary>
    /// Generates only the requested round. The round engine uses this hot path
    /// so later rounds do not repeatedly scan and iterate every prior/future round.
    /// </summary>
    public Task GenerateRoundFlagsAsync(Guid competitionId, int roundNumber, CancellationToken ct = default)
        => GenerateFlagsForRoundsAsync(competitionId, [roundNumber], ct);

    private async Task GenerateFlagsForRoundsAsync(
        Guid competitionId,
        IReadOnlyCollection<int> roundNumbers,
        CancellationToken ct)
    {
        var requestedRounds = roundNumbers.Where(round => round > 0).Distinct().Order().ToArray();
        if (requestedRounds.Length == 0)
            return;

        var competition = await _db.Competitions
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(c => c.Id == competitionId, ct);

        if (competition is null)
        {
            _logger.LogWarning("GenerateFlagsAsync: competition {CompetitionId} not found.", competitionId);
            return;
        }

        var teams = await _db.Teams
            .IgnoreQueryFilters()
            .Where(t =>
                t.CompetitionId == competitionId &&
                t.RegistrationStatus == TeamRegistrationStatus.Approved &&
                !t.IsBanned)
            .ToListAsync(ct);

        var challenges = await _db.Challenges
            .IgnoreQueryFilters()
            .Where(c => c.CompetitionId == competitionId && !c.IsDeleting)
            .ToListAsync(ct);

        // Load existing flags to avoid duplicates (unique constraint guard)
        var existingFlagKeys = await _db.AwdFlags
            .IgnoreQueryFilters()
            .Where(f => f.CompetitionId == competitionId && requestedRounds.Contains(f.RoundNumber))
            .Select(f => new { f.TeamId, f.ChallengeId, f.RoundNumber })
            .ToListAsync(ct);

        var existingKeys = existingFlagKeys
            .Select(x => (x.TeamId, x.ChallengeId, x.RoundNumber))
            .ToHashSet();

        var flagFormat = competition.FlagFormat ?? "flag{{{0}}}";
        var now = DateTime.UtcNow;
        var newFlags = new List<AwdFlag>();

        foreach (var team in teams)
        {
            foreach (var challenge in challenges)
            {
                if (string.IsNullOrWhiteSpace(challenge.FlagSecret))
                {
                    _logger.LogWarning(
                        "GenerateFlagsAsync: challenge {ChallengeId} has no FlagSecret; skipping AWD flags.",
                        challenge.Id);
                    continue;
                }

                foreach (var round in requestedRounds)
                {
                    if (existingKeys.Contains((team.Id, challenge.Id, round)))
                        continue;

                    var content = ComputeFlag(team.Id, challenge.Id, round, challenge.FlagSecret);
                    var flagValue = string.Format(flagFormat, content);

                    newFlags.Add(new AwdFlag
                    {
                        Id = Guid.NewGuid(),
                        CompetitionId = competitionId,
                        TeamId = team.Id,
                        ChallengeId = challenge.Id,
                        RoundNumber = round,
                        FlagContent = flagValue,
                        CreatedAt = now
                    });
                }
            }
        }

        if (newFlags.Count > 0)
        {
            _db.AwdFlags.AddRange(newFlags);
            await _db.SaveChangesAsync(ct);
            _logger.LogInformation(
                "GenerateFlagsAsync: generated {Count} flags for competition {CompetitionId}.",
                newFlags.Count, competitionId);
        }
    }

    /// <summary>
    /// For each (team, challenge) in the competition, destroys the existing container (if any)
    /// and creates a new one with the round's flag injected as NOCTF_FLAG.
    /// </summary>
    public async Task RefreshFlagsAsync(Guid competitionId, int roundNumber, CancellationToken ct = default)
    {
        await RefreshRoundFlagsAsync(competitionId, roundNumber, ct);
    }

    /// <summary>
    /// Refreshes a round and reports whether every eligible runtime reached its
    /// durable target state. The round engine uses this result as a recovery
    /// checkpoint; the public compatibility wrapper retains best-effort behavior.
    /// </summary>
    internal async Task<bool> RefreshRoundFlagsAsync(
        Guid competitionId,
        int roundNumber,
        CancellationToken ct = default)
    {
        var competitionEndTime = await _db.Competitions
            .IgnoreQueryFilters()
            .Where(c => c.Id == competitionId)
            .Select(c => (DateTime?)c.EndTime)
            .FirstOrDefaultAsync(ct);
        if (competitionEndTime is null || competitionEndTime <= DateTime.UtcNow)
        {
            _logger.LogWarning(
                "RefreshFlagsAsync: competition {CompetitionId} is missing or has already ended.",
                competitionId);
            return true;
        }

        var activeTeamIds = await _db.Teams
            .IgnoreQueryFilters()
            .Where(t =>
                t.CompetitionId == competitionId &&
                t.RegistrationStatus == TeamRegistrationStatus.Approved &&
                !t.IsBanned)
            .Select(t => t.Id)
            .ToListAsync(ct);

        var flags = await _db.AwdFlags
            .IgnoreQueryFilters()
            .Where(f =>
                f.CompetitionId == competitionId &&
                f.RoundNumber == roundNumber &&
                activeTeamIds.Contains(f.TeamId))
            .ToListAsync(ct);

        if (flags.Count == 0)
        {
            _logger.LogWarning(
                "RefreshFlagsAsync: no flags found for competition {CompetitionId} round {Round}.",
                competitionId, roundNumber);
            return true;
        }

        var challenges = await _db.Challenges
            .IgnoreQueryFilters()
            .Where(c => c.CompetitionId == competitionId && !c.IsDeleting)
            .ToListAsync(ct);

        var challengeMap = challenges.ToDictionary(c => c.Id);

        // Load or create game boxes for all (team, challenge) pairs
        var gameBoxes = await _db.AwdGameBoxes
            .IgnoreQueryFilters()
            .Where(g => g.CompetitionId == competitionId)
            .ToListAsync(ct);

        var gameBoxMap = gameBoxes.ToDictionary(g => (g.TeamId, g.ChallengeId));

        var refreshWork = new List<ContainerRefreshWork>(flags.Count);
        foreach (var flag in flags)
        {
            if (!challengeMap.TryGetValue(flag.ChallengeId, out var challenge))
                continue;

            var containerConfig = _runtimeConfigProvider.Build(challenge);
            if (containerConfig is null)
            {
                _logger.LogDebug(
                    "RefreshFlagsAsync: challenge {ChallengeId} has no container config, skipping.",
                    challenge.Id);
                continue;
            }

            // Merge NOCTF_FLAG into environment variables
            var envVars = new Dictionary<string, string>(
                containerConfig.EnvironmentVariables ?? new Dictionary<string, string>())
            {
                ["NOCTF_FLAG"] = flag.FlagContent
            };

            var newConfig = containerConfig with
            {
                EnvironmentVariables = envVars,
                Labels = new Dictionary<string, string>
                {
                    ["competitionId"] = competitionId.ToString(),
                    ["teamId"] = flag.TeamId.ToString(),
                    ["challengeId"] = flag.ChallengeId.ToString(),
                    ["app"] = "awd-challenge"
                },
                NetworkAliases = [BuildGameBoxAlias(flag.TeamId, flag.ChallengeId)],
                OperationId = flag.Id,
                Ttl = competitionEndTime.Value - DateTime.UtcNow
            };

            // Find or create game box
            if (!gameBoxMap.TryGetValue((flag.TeamId, flag.ChallengeId), out var gameBox))
            {
                gameBox = new AwdGameBox
                {
                    Id = Guid.NewGuid(),
                    CompetitionId = competitionId,
                    TeamId = flag.TeamId,
                    ChallengeId = flag.ChallengeId,
                    CreatedAt = DateTime.UtcNow
                };
                _db.AwdGameBoxes.Add(gameBox);
                gameBoxMap[(flag.TeamId, flag.ChallengeId)] = gameBox;
                gameBox.RuntimeOperationId = flag.Id;
            }

            if (gameBox.ContainerInstanceId is not null &&
                gameBox.RuntimeOperationId == flag.Id &&
                gameBox.LastFlagRefreshedAt is not null)
            {
                // The same round may be delivered more than once. Reusing its
                // Runner operation after destroying the successful container
                // would return a receipt for the container we just destroyed.
                continue;
            }

            refreshWork.Add(new ContainerRefreshWork(flag, gameBox, newConfig));
        }

        // Make every new candidate row visible before crossing the container
        // boundary. Deletion commits its tombstone under RuntimePreparation;
        // after the re-check below it can therefore never miss an in-flight
        // runtime that is allowed to continue.
        await _db.SaveChangesAsync(ct);

        var acquiredWork = new List<LeasedContainerRefreshWork>(refreshWork.Count);
        var unavailableLeaseCount = 0;
        foreach (var work in refreshWork)
        {
            var instanceLease = await _executionLease.TryAcquireAsync(
                _db,
                CompetitionExecutionLeaseKeys.ChallengeInstance(work.Flag.TeamId, work.Flag.ChallengeId),
                competitionId,
                ct);
            if (instanceLease is null)
            {
                unavailableLeaseCount++;
                continue;
            }

            acquiredWork.Add(new LeasedContainerRefreshWork(work, instanceLease));
        }

        try
        {
            var leasedWork = new List<LeasedContainerRefreshWork>(acquiredWork.Count);
            if (acquiredWork.Count > 0)
            {
                // Revalidate all candidates in three set-oriented queries while
                // holding one short lifecycle barrier. The previous per-box
                // implementation issued three queries and acquired the same
                // preparation lease for every team/challenge pair.
                await using var preparationLease = await _executionLease.TryAcquireAsync(
                    _db,
                    CompetitionExecutionLeaseKeys.RuntimePreparation,
                    competitionId,
                    ct);
                if (preparationLease is null)
                    return false;

                var lostTokens = acquiredWork
                    .Select(work => work.Lease.LostToken)
                    .Append(preparationLease.LostToken)
                    .Append(ct)
                    .ToArray();
                using var preparationCts = CancellationTokenSource.CreateLinkedTokenSource(lostTokens);
                var preparationCt = preparationCts.Token;
                var validationNow = DateTime.UtcNow;
                var competitionActive = await _db.Competitions
                    .IgnoreQueryFilters()
                    .AsNoTracking()
                    .AnyAsync(c =>
                        c.Id == competitionId &&
                        c.Status == CompetitionStatus.Running &&
                        c.StartTime <= validationNow &&
                        c.EndTime > validationNow,
                        preparationCt);
                if (!competitionActive)
                    return true;

                var candidateTeamIds = acquiredWork
                    .Select(work => work.Work.Flag.TeamId)
                    .Distinct()
                    .ToArray();
                var validTeamIds = (await _db.Teams
                        .IgnoreQueryFilters()
                        .AsNoTracking()
                        .Where(team =>
                            team.CompetitionId == competitionId &&
                            candidateTeamIds.Contains(team.Id) &&
                            team.RegistrationStatus == TeamRegistrationStatus.Approved &&
                            !team.IsBanned)
                        .Select(team => team.Id)
                        .ToListAsync(preparationCt))
                    .ToHashSet();
                var candidateChallengeIds = acquiredWork
                    .Select(work => work.Work.Flag.ChallengeId)
                    .Distinct()
                    .ToArray();
                var validChallengeIds = (await _db.Challenges
                        .IgnoreQueryFilters()
                        .AsNoTracking()
                        .Where(challenge =>
                            challenge.CompetitionId == competitionId &&
                            candidateChallengeIds.Contains(challenge.Id) &&
                            !challenge.IsDeleting)
                        .Select(challenge => challenge.Id)
                        .ToListAsync(preparationCt))
                    .ToHashSet();

                leasedWork.AddRange(acquiredWork.Where(work =>
                    validTeamIds.Contains(work.Work.Flag.TeamId) &&
                    validChallengeIds.Contains(work.Work.Flag.ChallengeId)));
            }

            var failedRefreshCount = 0;
            await Parallel.ForEachAsync(
                leasedWork,
                new ParallelOptions { MaxDegreeOfParallelism = _maxConcurrentRefreshes, CancellationToken = ct },
                async (work, token) =>
                {
                    using var workCts = CancellationTokenSource.CreateLinkedTokenSource(
                        token,
                        work.Lease.LostToken);
                    if (!await RefreshContainerAsync(
                            competitionId,
                            roundNumber,
                            work.Work,
                            workCts.Token))
                    {
                        Interlocked.Increment(ref failedRefreshCount);
                    }
                });

            return unavailableLeaseCount == 0 && failedRefreshCount == 0;
        }
        finally
        {
            try
            {
                // Persist successful external transitions before releasing the
                // per-instance leases, including during cooperative shutdown.
                await _db.SaveChangesAsync(CancellationToken.None);
            }
            finally
            {
                foreach (var work in acquiredWork)
                    await work.Lease.DisposeAsync();
            }
        }
    }

    private async Task<bool> RefreshContainerAsync(
        Guid competitionId,
        int roundNumber,
        ContainerRefreshWork work,
        CancellationToken ct)
    {
        var flag = work.Flag;
        var gameBox = work.GameBox;
        if (gameBox.ContainerInstanceId is not null)
        {
            try
            {
                var oldInstance = new ContainerInstance(
                    Id: Guid.NewGuid(),
                    CompetitionId: competitionId,
                    TeamId: flag.TeamId,
                    ChallengeId: flag.ChallengeId,
                    ProviderType: gameBox.ProviderType,
                    ContainerId: gameBox.ContainerInstanceId,
                    PortMappings: new Dictionary<int, int>(),
                    Status: "running",
                    StartedAt: DateTime.UtcNow,
                    OrchestrationNamespace: gameBox.OrchestrationNamespace);
                await _containerManager.DestroyContainerAsync(oldInstance, ct);
                ClearRuntimeMetadata(gameBox);
                gameBox.RuntimeOperationId = flag.Id;
                gameBox.LastInstanceActionAt = DateTime.UtcNow;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "RefreshFlagsAsync: failed to destroy old container {ContainerId} for team {TeamId} challenge {ChallengeId}.",
                    gameBox.ContainerInstanceId,
                    flag.TeamId,
                    flag.ChallengeId);
                return false;
            }
        }

        try
        {
            gameBox.RuntimeOperationId = flag.Id;
            var newInstance = await _containerManager.CreateContainerAsync(work.Config, ct);
            gameBox.ContainerInstanceId = newInstance.ContainerId;
            gameBox.ProviderType = newInstance.ProviderType;
            gameBox.PublicHost = newInstance.PublicHost;
            gameBox.EntryUrl = newInstance.EntryUrl;
            gameBox.OrchestrationNamespace = newInstance.OrchestrationNamespace;
            gameBox.PortMappingsJson = JsonSerializer.Serialize(newInstance.PortMappings);
            gameBox.RuntimeKind = "container";
            gameBox.InternalHost = newInstance.InternalHost;
            gameBox.InternalPortMappingsJson = JsonSerializer.Serialize(newInstance.InternalPortMappings ?? []);
            gameBox.ExpiresAt = newInstance.ExpectedStopAt ??
                (work.Config.Ttl is { } ttl ? DateTime.UtcNow.Add(ttl) : null);
            gameBox.LastInstanceActionAt = DateTime.UtcNow;
            gameBox.LastFlagRefreshedAt = DateTime.UtcNow;

            _logger.LogInformation(
                "RefreshFlagsAsync: created container {ContainerId} for team {TeamId} challenge {ChallengeId} round {Round}.",
                newInstance.ContainerId,
                flag.TeamId,
                flag.ChallengeId,
                roundNumber);
            return true;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "RefreshFlagsAsync: failed to create container for team {TeamId} challenge {ChallengeId}.",
                flag.TeamId,
                flag.ChallengeId);
            return false;
        }
    }

    private static void ClearRuntimeMetadata(AwdGameBox gameBox)
    {
        gameBox.ContainerInstanceId = null;
        gameBox.ProviderType = "docker";
        gameBox.PublicHost = null;
        gameBox.EntryUrl = null;
        gameBox.OrchestrationNamespace = null;
        gameBox.PortMappingsJson = "{}";
        gameBox.RuntimeKind = "container";
        gameBox.ComposeProjectName = null;
        gameBox.ComposeYaml = null;
        gameBox.InternalHost = null;
        gameBox.InternalPortMappingsJson = "{}";
        gameBox.ExpiresAt = null;
    }

    internal static string BuildGameBoxAlias(Guid teamId, Guid challengeId)
        => $"gamebox-{teamId:N}"[..16] + $"-{challengeId:N}"[..9];

    /// <summary>
    /// Computes HMAC-SHA256(key=roundSecret, message=teamId+challengeId+roundNumber),
    /// takes first 16 bytes, and returns Base64url-encoded result.
    /// </summary>
    public static string ComputeFlag(Guid teamId, Guid challengeId, int roundNumber, string? roundSecret)
    {
        if (string.IsNullOrWhiteSpace(roundSecret))
            throw new InvalidOperationException("AWD challenge FlagSecret is required to generate round flags.");

        var secret = roundSecret.Trim();
        var keyBytes = Encoding.UTF8.GetBytes(secret);
        var message = Encoding.UTF8.GetBytes(
            teamId.ToString("N") + challengeId.ToString("N") + roundNumber.ToString());

        using var hmac = new HMACSHA256(keyBytes);
        var hash = hmac.ComputeHash(message);

        // Take first 16 bytes and Base64-encode
        return Convert.ToBase64String(hash, 0, 16)
            .Replace('+', '-')
            .Replace('/', '_')
            .TrimEnd('=');
    }

    private sealed record ContainerRefreshWork(AwdFlag Flag, AwdGameBox GameBox, ContainerConfig Config);
    private sealed record LeasedContainerRefreshWork(ContainerRefreshWork Work, IExecutionLease Lease);
}
