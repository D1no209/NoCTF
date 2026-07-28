using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Challenges.Flags;
using NoCTF.Application.Runtime.Ports;
using NoCTF.Domain.Challenges;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Teams;

namespace NoCTF.Infrastructure.Persistence.UseCaseAdapters;

public sealed class PostgresMissingFlagGenerator(
    NoCtfDbContext db,
    IChallengeRuntimeTemplateCatalog templates,
    IPerTeamRuntimeFlagStore runtimeFlags) : IMissingFlagGenerator
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
            var usesPerTeamRuntimeFlag = competition.Mode == GameMode.Ctf
                && templates.Get(competition.Mode, challenge.ConfigurationJson)?.FlagSource
                    == RuntimeFlagSource.PerTeam;
            if (competition.Mode == GameMode.Ctf && !usesPerTeamRuntimeFlag)
                continue;
            foreach (var teamId in teams)
            {
                if (usesPerTeamRuntimeFlag)
                {
                    try
                    {
                        _ = await runtimeFlags.EnsureAsync(
                            competitionId,
                            challenge.Id,
                            teamId,
                            now,
                            ct);
                    }
                    catch (Exception exception)
                        when (exception is CryptographicException
                            or InvalidOperationException
                            or OverflowException)
                    {
                        failures.Add(new(
                            challenge.Id,
                            teamId,
                            "flag_generation_failed",
                            "The flag could not be derived."));
                    }
                    continue;
                }

                var exists = await db.ChallengeFlags.AnyAsync(flag =>
                    flag.CompetitionChallengeId == challenge.Id &&
                    flag.TeamId == teamId &&
                    flag.SpecificationKind == SpecificationKind.RuntimeDefinition &&
                    flag.SpecificationId == challenge.Id &&
                    flag.DeletedAt == null, ct);
                if (exists)
                    continue;
                try
                {
                    var flag = PerTeamRuntimeFlagDerivation.Derive(
                        competition.FlagDerivationSecret,
                        competitionId,
                        challenge.Id,
                        teamId);
                    db.ChallengeFlags.Add(new ChallengeFlag
                    {
                        Id = Guid.CreateVersion7(now),
                        CompetitionChallengeId = challenge.Id,
                        TeamId = teamId,
                        SpecificationKind = SpecificationKind.RuntimeDefinition,
                        SpecificationId = challenge.Id,
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
}
