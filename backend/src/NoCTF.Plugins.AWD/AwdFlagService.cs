using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NoCTF.Core;
using NoCTF.Infrastructure;
using NoCTF.PluginBase;

namespace NoCTF.Plugins.AWD;

/// <summary>
/// Generates deterministic per-round flags for AWD competitions and injects them
/// into running containers by recreating the container with the NOCTF_FLAG env var.
/// </summary>
public class AwdFlagService
{
    private readonly ApplicationDbContext _db;
    private readonly IContainerManager _containerManager;
    private readonly IEnumerable<IChallengeType> _challengeTypes;
    private readonly ILogger<AwdFlagService> _logger;

    public AwdFlagService(
        ApplicationDbContext db,
        IContainerManager containerManager,
        IEnumerable<IChallengeType> challengeTypes,
        ILogger<AwdFlagService> logger)
    {
        _db = db;
        _containerManager = containerManager;
        _challengeTypes = challengeTypes;
        _logger = logger;
    }

    /// <summary>
    /// Idempotently generates flags for all (team, challenge, round) combinations
    /// for the given competition. Already-existing flags are skipped.
    /// </summary>
    public async Task GenerateFlagsAsync(Guid competitionId, int totalRounds, CancellationToken ct = default)
    {
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
            .Where(c => c.CompetitionId == competitionId)
            .ToListAsync(ct);

        // Load existing flags to avoid duplicates (unique constraint guard)
        var existingFlagKeys = await _db.AwdFlags
            .IgnoreQueryFilters()
            .Where(f => f.CompetitionId == competitionId)
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

                for (int round = 1; round <= totalRounds; round++)
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
            return;
        }

        var challenges = await _db.Challenges
            .IgnoreQueryFilters()
            .Where(c => c.CompetitionId == competitionId)
            .ToListAsync(ct);

        var challengeMap = challenges.ToDictionary(c => c.Id);

        // Load or create game boxes for all (team, challenge) pairs
        var gameBoxes = await _db.AwdGameBoxes
            .IgnoreQueryFilters()
            .Where(g => g.CompetitionId == competitionId)
            .ToListAsync(ct);

        var gameBoxMap = gameBoxes.ToDictionary(g => (g.TeamId, g.ChallengeId));

        foreach (var flag in flags)
        {
            if (!challengeMap.TryGetValue(flag.ChallengeId, out var challenge))
                continue;

            var challengeType = _challengeTypes.FirstOrDefault(ct2 => ct2.TypeId == challenge.TypeId);
            if (challengeType is null)
            {
                _logger.LogWarning(
                    "RefreshFlagsAsync: no IChallengeType found for TypeId={TypeId}.", challenge.TypeId);
                continue;
            }

            var challengeContext = new ChallengeContext(
                ChallengeId: challenge.Id,
                CompetitionId: competitionId,
                FlagSecret: challenge.FlagSecret ?? string.Empty,
                Configuration: new Dictionary<string, string>());

            var containerConfig = challengeType.GetContainerConfig(challengeContext);
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

            var newConfig = containerConfig with { EnvironmentVariables = envVars };

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
            }

            // Destroy old container if one exists
            if (gameBox.ContainerInstanceId is not null)
            {
                try
                {
                    // Build a minimal ContainerInstance to pass to DestroyContainerAsync
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
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex,
                        "RefreshFlagsAsync: failed to destroy old container {ContainerId} for team {TeamId} challenge {ChallengeId}.",
                        gameBox.ContainerInstanceId, flag.TeamId, flag.ChallengeId);
                    continue;
                }
            }

            // Create new container
            try
            {
                var newInstance = await _containerManager.CreateContainerAsync(newConfig, ct);
                gameBox.ContainerInstanceId = newInstance.ContainerId;
                gameBox.ProviderType = newInstance.ProviderType;
                gameBox.PublicHost = newInstance.PublicHost;
                gameBox.EntryUrl = newInstance.EntryUrl;
                gameBox.OrchestrationNamespace = newInstance.OrchestrationNamespace;
                gameBox.LastFlagRefreshedAt = DateTime.UtcNow;

                _logger.LogInformation(
                    "RefreshFlagsAsync: created container {ContainerId} for team {TeamId} challenge {ChallengeId} round {Round}.",
                    newInstance.ContainerId, flag.TeamId, flag.ChallengeId, roundNumber);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "RefreshFlagsAsync: failed to create container for team {TeamId} challenge {ChallengeId}.",
                    flag.TeamId, flag.ChallengeId);
            }
        }

        await _db.SaveChangesAsync(ct);
    }

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
}
