using Microsoft.EntityFrameworkCore;
using NoCTF.Core;
using NoCTF.Infrastructure;

namespace NoCTF.Plugins.AWDP;

public sealed record AwdpChallengeConfig(
    int AttackScorePerRound,
    int DefenseScorePerRound,
    int MaxAttackAttempts,
    int MaxDefenseAttempts,
    bool AllowAttackAfterBreakSuccess,
    bool AllowDefenseAfterFixSuccess,
    bool ServicePenaltyEnabled,
    int ServicePenaltyPerRound,
    bool ViolationPenaltyEnabled,
    int ViolationPenalty,
    string FixEntry,
    int FixTimeoutSeconds);

public class AwdpConfigResolver(ApplicationDbContext db)
{
    public async Task<AwdpChallengeConfig> ResolveAsync(Guid competitionId, Guid challengeId, CancellationToken ct = default)
    {
        var competition = await db.Competitions
            .IgnoreQueryFilters()
            .AsNoTracking()
            .FirstAsync(c => c.Id == competitionId, ct);

        var challenge = await db.Challenges
            .IgnoreQueryFilters()
            .AsNoTracking()
            .FirstAsync(c =>
                c.Id == challengeId &&
                c.CompetitionId == competitionId &&
                !c.IsDeleting,
                ct);

        return Resolve(competition, challenge);
    }

    public static AwdpChallengeConfig Resolve(Competition competition, Challenge challenge)
    {
        var fixEntry = FirstNonBlank(challenge.AwdpFixEntry, competition.AwdpFixEntry, "fix.sh");
        return new AwdpChallengeConfig(
            AttackScorePerRound: PositiveOrDefault(
                challenge.AwdpAttackScorePerRound,
                competition.AwdpAttackScorePerRound,
                null,
                50),
            DefenseScorePerRound: PositiveOrDefault(
                challenge.AwdpDefenseScorePerRound,
                competition.AwdpDefenseScorePerRound,
                null,
                100),
            MaxAttackAttempts: PositiveOrDefault(
                challenge.AwdpMaxAttackAttempts,
                competition.AwdpMaxAttackAttempts,
                null,
                5),
            MaxDefenseAttempts: PositiveOrDefault(
                challenge.AwdpMaxDefenseAttempts,
                competition.AwdpMaxDefenseAttempts,
                null,
                3),
            AllowAttackAfterBreakSuccess: competition.AwdpAllowAttackAfterBreakSuccess ?? false,
            AllowDefenseAfterFixSuccess: competition.AwdpAllowDefenseAfterFixSuccess ?? false,
            ServicePenaltyEnabled: competition.AwdpServicePenaltyEnabled ?? false,
            ServicePenaltyPerRound: PositiveOrDefault(
                competition.AwdpServicePenaltyPerRound,
                competition.ServiceDownPenalty,
                null,
                0),
            ViolationPenaltyEnabled: competition.AwdpViolationPenaltyEnabled ?? false,
            ViolationPenalty: PositiveOrDefault(
                competition.AwdpViolationPenalty,
                null,
                null,
                0),
            FixEntry: fixEntry,
            FixTimeoutSeconds: PositiveOrDefault(
                challenge.AwdpFixTimeoutSeconds,
                competition.AwdpFixTimeoutSeconds,
                challenge.CheckerConfig?.TimeoutSeconds,
                60));
    }

    private static int PositiveOrDefault(int? first, int? second, int? third, int fallback)
    {
        foreach (var value in new[] { first, second, third })
        {
            if (value is > 0)
                return value.Value;
        }

        return fallback;
    }

    private static string FirstNonBlank(params string?[] values)
    {
        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
                return value.Trim();
        }

        return "fix.sh";
    }
}

public class AwdpStateService(ApplicationDbContext db)
{
    public async Task<AwdpTeamChallengeState> GetOrCreateAsync(
        Guid competitionId,
        Guid teamId,
        Guid challengeId,
        CancellationToken ct = default)
    {
        var state = await db.AwdpTeamChallengeStates
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(s =>
                s.CompetitionId == competitionId &&
                s.TeamId == teamId &&
                s.ChallengeId == challengeId, ct);

        if (state is not null)
            return state;

        var now = DateTime.UtcNow;
        state = new AwdpTeamChallengeState
        {
            Id = Guid.NewGuid(),
            CompetitionId = competitionId,
            TeamId = teamId,
            ChallengeId = challengeId,
            CreatedAt = now,
            UpdatedAt = now
        };

        db.AwdpTeamChallengeStates.Add(state);
        return state;
    }
}
