using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Challenges.Flags;
using NoCTF.Domain.Challenges;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Teams;

namespace NoCTF.Infrastructure.Persistence.UseCaseAdapters;

public sealed class PostgresMissingFlagGenerator(NoCtfDbContext db) : IMissingFlagGenerator
{
    public async Task<IReadOnlyList<MissingFlagGenerationFailure>> GenerateAsync(
        Guid competitionId,
        DateTimeOffset now,
        CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        if (await CompetitionWriteLock.AcquireAsync(db, competitionId, ct) is null)
            return [new(Guid.Empty, Guid.Empty, "competition_not_found", "Competition was not found.")];
        var competition = await db.Competitions.SingleAsync(item => item.Id == competitionId, ct);
        if (competition.Mode is not (GameMode.Ctf or GameMode.Koh))
            return [new(Guid.Empty, Guid.Empty, "game_mode_unsupported", "Only CTF and KoH use this generator.")];
        var challenges = await db.CompetitionChallenges.AsNoTracking()
            .Where(item => item.CompetitionId == competitionId && item.IsPublished)
            .Select(item => new
            {
                item.Id,
                item.ChallengeId,
                item.ConfigurationJson
            })
            .ToListAsync(ct);
        var teams = await db.Teams.AsNoTracking()
            .Where(item =>
                item.CompetitionId == competitionId &&
                item.RegistrationStatus == TeamRegistrationStatus.Approved &&
                !item.IsBanned)
            .Select(item => item.Id)
            .ToListAsync(ct);
        var failures = new List<MissingFlagGenerationFailure>();
        foreach (var challenge in challenges)
        {
            if (competition.Mode == GameMode.Ctf && !UsesPerTeamFlag(challenge.ConfigurationJson))
                continue;
            foreach (var teamId in teams)
            {
                var exists = await db.ChallengeFlags.AnyAsync(flag =>
                    flag.CompetitionChallengeId == challenge.Id &&
                    flag.TeamId == teamId &&
                    flag.DeletedAt == null, ct);
                if (exists)
                    continue;
                try
                {
                    var flag = Derive(
                        competition.FlagDerivationSecret,
                        competitionId,
                        challenge.Id,
                        teamId);
                    db.ChallengeFlags.Add(new ChallengeFlag
                    {
                        Id = Guid.CreateVersion7(now),
                        CompetitionChallengeId = challenge.Id,
                        TeamId = teamId,
                        Flag = flag,
                        FlagSha256 = ManageChallengeFlags.Hash(flag),
                        CreatedAt = now
                    });
                }
                catch (Exception exception) when (exception is CryptographicException or OverflowException)
                {
                    failures.Add(new(
                        challenge.Id,
                        teamId,
                        "flag_generation_failed",
                        "The flag could not be derived."));
                }
            }
        }
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return failures;
    }

    private static bool UsesPerTeamFlag(string json)
    {
        using var document = JsonDocument.Parse(json);
        if (!document.RootElement.TryGetProperty("flagSource", out var value) &&
            !document.RootElement.TryGetProperty("FlagSource", out value))
            return false;
        return string.Equals(value.GetString(), "PerTeam", StringComparison.OrdinalIgnoreCase);
    }

    private static string Derive(
        byte[] secret,
        Guid competitionId,
        Guid competitionChallengeId,
        Guid teamId)
    {
        var message = Encoding.UTF8.GetBytes(
            $"noctf:teamhash:v1:{competitionId:D}:{competitionChallengeId:D}:{teamId:D}");
        var hash = HMACSHA256.HashData(secret, message);
        return $"flag{{{Convert.ToHexStringLower(hash)[..32]}}}";
    }
}
