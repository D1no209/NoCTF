using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using NoCTF.Core;
using NoCTF.Infrastructure;

namespace NoCTF.Plugins.Penetration;

public sealed record PenetrationFlagMatch(
    PenetrationFlag? Flag,
    DynamicFlagInstance? DynamicFlag,
    Guid? VictimTeamId,
    bool IsCorrect,
    bool IsCrossTeamDynamicFlag);

public class PenetrationFlagService(ApplicationDbContext db)
{
    public async Task<List<DynamicFlagInstance>> RegenerateDynamicFlagsAsync(
        Challenge challenge,
        TeamChallengeInstance instance,
        IReadOnlyCollection<PenetrationFlag> flags,
        CancellationToken ct)
    {
        var activeFlags = db.DynamicFlagInstances
            .IgnoreQueryFilters()
            .Where(f =>
                f.CompetitionId == challenge.CompetitionId &&
                f.TeamId == instance.TeamId &&
                f.ChallengeId == challenge.Id &&
                f.IsActive);
        if (db.Database.IsRelational())
        {
            // Deactivate in one statement before inserting replacements. This
            // avoids loading every historical flag and guarantees the partial
            // unique active-flag index cannot observe old and new rows together.
            await activeFlags.ExecuteUpdateAsync(
                setters => setters.SetProperty(flag => flag.IsActive, false),
                ct);
            foreach (var entry in db.ChangeTracker.Entries<DynamicFlagInstance>().Where(entry =>
                         entry.Entity.CompetitionId == challenge.CompetitionId &&
                         entry.Entity.TeamId == instance.TeamId &&
                         entry.Entity.ChallengeId == challenge.Id &&
                         entry.Entity.IsActive))
            {
                entry.Entity.IsActive = false;
            }
        }
        else
        {
            var oldFlags = await activeFlags.ToListAsync(ct);
            foreach (var oldFlag in oldFlags)
                oldFlag.IsActive = false;
            await db.SaveChangesAsync(ct);
        }

        var now = DateTime.UtcNow;
        var generated = flags
            .Where(f => f.IsDynamic)
            .Select(flag =>
            {
                var rawValue = GenerateRandomValue();
                return new DynamicFlagInstance
                {
                    Id = Guid.NewGuid(),
                    CompetitionId = challenge.CompetitionId,
                    ChallengeId = challenge.Id,
                    TeamId = instance.TeamId,
                    FlagId = flag.Id,
                    InstanceId = instance.Id,
                    ValueSecret = "[REDACTED]",
                    PlainValue = rawValue,
                    // Store one canonical digest so matching can use an indexed lookup.
                    // Submitted formatted flags are normalized back to their raw value.
                    ValueHash = Hash(rawValue),
                    IsActive = true,
                    GeneratedAt = now,
                };
            })
            .ToList();

        db.DynamicFlagInstances.AddRange(generated);
        await db.SaveChangesAsync(ct);
        return generated;
    }

    public async Task<PenetrationFlagMatch> MatchAsync(
        Challenge challenge,
        TeamChallengeInstance instance,
        string submittedFlag,
        CancellationToken ct)
    {
        var flags = await db.PenetrationFlags
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(f => f.CompetitionId == challenge.CompetitionId && f.ChallengeId == challenge.Id)
            .OrderBy(f => f.Stage)
            .ToListAsync(ct);

        var submittedHashes = SubmittedFlagCandidates(challenge, submittedFlag)
            .Select(Hash)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var dynamicFlags = await db.DynamicFlagInstances
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(f =>
                f.CompetitionId == challenge.CompetitionId &&
                f.ChallengeId == challenge.Id &&
                f.IsActive &&
                submittedHashes.Contains(f.ValueHash))
            .ToListAsync(ct);

        foreach (var flag in flags)
        {
            if (flag.IsDynamic)
            {
                var dynamicFlag = dynamicFlags.FirstOrDefault(f =>
                    f.TeamId == instance.TeamId &&
                    f.InstanceId == instance.Id &&
                    f.FlagId == flag.Id);
                if (dynamicFlag is not null && MatchesSubmittedHash(challenge, submittedFlag, dynamicFlag.ValueHash))
                    return new PenetrationFlagMatch(flag, dynamicFlag, null, true, false);
            }
            else if (MatchesStoredStaticFlag(challenge, submittedFlag, flag))
            {
                return new PenetrationFlagMatch(flag, null, null, true, false);
            }
        }

        foreach (var dynamicFlag in dynamicFlags.Where(f => f.TeamId != instance.TeamId))
        {
            if (!MatchesSubmittedHash(challenge, submittedFlag, dynamicFlag.ValueHash)) continue;
            var flag = flags.FirstOrDefault(f => f.Id == dynamicFlag.FlagId);
            return new PenetrationFlagMatch(flag, dynamicFlag, dynamicFlag.TeamId, false, true);
        }

        return new PenetrationFlagMatch(null, null, null, false, false);
    }

    public async Task MarkSolvedAsync(
        Guid competitionId,
        Guid teamId,
        Guid challengeId,
        Guid flagId,
        DateTime solvedAt,
        CancellationToken ct)
    {
        var dynamicFlag = await db.DynamicFlagInstances
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(f =>
                f.CompetitionId == competitionId &&
                f.TeamId == teamId &&
                f.ChallengeId == challengeId &&
                f.FlagId == flagId &&
                f.IsActive,
                ct);
        if (dynamicFlag is not null)
            dynamicFlag.SolvedAt = solvedAt;

        if (db.Database.IsRelational())
        {
            await db.PenetrationFlags
                .IgnoreQueryFilters()
                .Where(f => f.CompetitionId == competitionId && f.Id == flagId)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(f => f.SolvedCount, f => f.SolvedCount + 1)
                    .SetProperty(f => f.UpdatedAt, solvedAt), ct);
        }
        else
        {
            var flag = await db.PenetrationFlags
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(f => f.CompetitionId == competitionId && f.Id == flagId, ct);
            if (flag is not null)
            {
                flag.SolvedCount += 1;
                flag.UpdatedAt = solvedAt;
            }
        }
    }

    public static string FormatFlag(Challenge challenge, string content)
    {
        var prefix = string.IsNullOrWhiteSpace(challenge.FlagPrefix) ? "flag" : challenge.FlagPrefix.Trim();
        if (prefix.Contains("{0}", StringComparison.Ordinal))
            return string.Format(CultureInfo.InvariantCulture, prefix, content);

        if (prefix.Contains("{}", StringComparison.Ordinal))
            return prefix.Replace("{}", $"{{{content}}}", StringComparison.Ordinal);

        var braceIndex = prefix.IndexOf('{', StringComparison.Ordinal);
        if (braceIndex >= 0)
            prefix = prefix[..braceIndex].Trim();

        return $"{prefix}{{{content}}}";
    }

    public static string Hash(string value)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    public static string RedactSubmittedFlag(string submittedFlag)
        => $"sha256:{Hash(submittedFlag)};len:{submittedFlag.Length}";

    public static bool TimingSafeEquals(string submitted, string expected)
    {
        var submittedBytes = Encoding.UTF8.GetBytes(submitted);
        var expectedBytes = Encoding.UTF8.GetBytes(expected);
        return CryptographicOperations.FixedTimeEquals(submittedBytes, expectedBytes);
    }

    private static bool MatchesSubmitted(Challenge challenge, string submittedFlag, string content)
        => TimingSafeEquals(submittedFlag, content) ||
           TimingSafeEquals(submittedFlag, FormatFlag(challenge, content));

    private static bool MatchesStoredStaticFlag(Challenge challenge, string submittedFlag, PenetrationFlag flag)
    {
        if (!string.IsNullOrWhiteSpace(flag.ValueHash) &&
            MatchesSubmittedHash(challenge, submittedFlag, flag.ValueHash))
            return true;

        return !string.IsNullOrWhiteSpace(flag.ValueSecret) &&
               MatchesSubmitted(challenge, submittedFlag, flag.ValueSecret);
    }

    private static bool MatchesSubmittedHash(Challenge challenge, string submittedFlag, string? storedHash)
    {
        if (string.IsNullOrWhiteSpace(storedHash))
            return false;

        var hashes = storedHash
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var candidate in SubmittedFlagCandidates(challenge, submittedFlag))
        {
            var candidateHash = Hash(candidate);
            if (hashes.Any(hash => TimingSafeEquals(candidateHash, hash)))
                return true;
        }

        return false;
    }

    private static IEnumerable<string> SubmittedFlagCandidates(Challenge challenge, string submittedFlag)
    {
        yield return submittedFlag;

        var configuredPrefix = string.IsNullOrWhiteSpace(challenge.FlagPrefix) ? "flag" : challenge.FlagPrefix.Trim();
        if (configuredPrefix.Contains("{0}", StringComparison.Ordinal))
        {
            var parts = configuredPrefix.Split("{0}", StringSplitOptions.None);
            if (parts.Length == 2 &&
                submittedFlag.StartsWith(parts[0], StringComparison.Ordinal) &&
                submittedFlag.EndsWith(parts[1], StringComparison.Ordinal) &&
                submittedFlag.Length >= parts[0].Length + parts[1].Length)
            {
                yield return submittedFlag[parts[0].Length..^parts[1].Length];
            }
        }

        if (configuredPrefix.Contains("{}", StringComparison.Ordinal))
        {
            var parts = configuredPrefix.Split("{}", StringSplitOptions.None);
            if (parts.Length == 2)
            {
                var before = $"{parts[0]}{{";
                var after = $"}}{parts[1]}";
                if (submittedFlag.StartsWith(before, StringComparison.Ordinal) &&
                    submittedFlag.EndsWith(after, StringComparison.Ordinal) &&
                    submittedFlag.Length >= before.Length + after.Length)
                {
                    yield return submittedFlag[before.Length..^after.Length];
                }
            }
        }

        var prefix = configuredPrefix;
        var braceIndex = prefix.IndexOf('{', StringComparison.Ordinal);
        if (braceIndex >= 0)
            prefix = prefix[..braceIndex].Trim();

        var wrappedPrefix = $"{prefix}{{";
        if (submittedFlag.StartsWith(wrappedPrefix, StringComparison.Ordinal) &&
            submittedFlag.EndsWith("}", StringComparison.Ordinal) &&
            submittedFlag.Length > wrappedPrefix.Length + 1)
        {
            yield return submittedFlag[wrappedPrefix.Length..^1];
        }
    }

    private static string GenerateRandomValue()
    {
        var bytes = RandomNumberGenerator.GetBytes(16);
        return new Guid(bytes).ToString("D");
    }
}
