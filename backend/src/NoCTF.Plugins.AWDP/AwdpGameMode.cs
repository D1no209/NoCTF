using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Scoring;
using NoCTF.Core;
using NoCTF.Infrastructure;
using NoCTF.PluginBase;

namespace NoCTF.Plugins.AWDP;

/// <summary>
/// AWDP Break handler. Successful Breaks only change state; points are awarded by
/// AWDP round settlement.
/// </summary>
public class AwdpGameMode(
    ApplicationDbContext db,
    AwdpConfigResolver configResolver,
    AwdpStateService stateService) : IGameMode
{
    public GameModeType Type => GameModeType.Awdp;

    public Task InitializeAsync(GameContext context, CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    public Task OnRoundTickAsync(GameContext context, CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    public async Task<SubmissionResult> ProcessSubmissionAsync(
        SubmissionContext context,
        CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        var competition = await db.Competitions
            .IgnoreQueryFilters()
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == context.CompetitionId, cancellationToken);

        if (competition is null || competition.GameModeType != GameModeType.Awdp)
            return SubmissionResult.NotImplemented;

        if (now < competition.StartTime || competition.Status == CompetitionStatus.Draft)
            return SubmissionResult.CompetitionNotStarted;

        if (now > competition.EndTime || competition.Status == CompetitionStatus.Finished)
            return SubmissionResult.CompetitionEnded;

        if (competition.Status == CompetitionStatus.Paused)
            return SubmissionResult.CompetitionPaused;

        var challenge = await db.Challenges
            .IgnoreQueryFilters()
            .AsNoTracking()
            .FirstOrDefaultAsync(c =>
                c.CompetitionId == context.CompetitionId &&
                c.Id == context.ChallengeId, cancellationToken);

        if (challenge is null)
            return SubmissionResult.WrongFlag;

        var config = await configResolver.ResolveAsync(context.CompetitionId, context.ChallengeId, cancellationToken);
        var state = await stateService.GetOrCreateAsync(
            context.CompetitionId,
            context.TeamId,
            context.ChallengeId,
            cancellationToken);
        var gameBox = await db.AwdGameBoxes
            .IgnoreQueryFilters()
            .AsNoTracking()
            .FirstOrDefaultAsync(g =>
                g.CompetitionId == context.CompetitionId &&
                g.TeamId == context.TeamId &&
                g.ChallengeId == context.ChallengeId, cancellationToken);

        if (gameBox?.ContainerInstanceId is null)
        {
            state.InstanceStatus = AwdpInstanceStatus.InstanceNotCreated;
            state.UpdatedAt = now;
            await db.SaveChangesAsync(cancellationToken);
            return SubmissionResult.InstanceRequired;
        }

        if (gameBox.ExpiresAt is not null && gameBox.ExpiresAt <= now)
        {
            state.InstanceStatus = AwdpInstanceStatus.InstanceExpired;
            state.UpdatedAt = now;
            await db.SaveChangesAsync(cancellationToken);
            return SubmissionResult.InstanceExpired;
        }

        state.InstanceStatus = AwdpInstanceStatus.InstanceRunning;

        if (state.BreakStatus == AwdpBreakStatus.BreakSuccess && !config.AllowAttackAfterBreakSuccess)
            return SubmissionResult.AlreadySolved;

        if (state.AttackAttempts >= config.MaxAttackAttempts)
        {
            state.BreakStatus = AwdpBreakStatus.AttackAttemptsExhausted;
            state.UpdatedAt = now;
            await db.SaveChangesAsync(cancellationToken);
            return SubmissionResult.AttemptsExhausted;
        }

        state.AttackAttempts += 1;
        state.LastBreakSubmittedAt = now;
        state.BreakStatus = AwdpBreakStatus.BreakSubmitted;
        state.UpdatedAt = now;

        var expectedFlag = await ResolveExpectedFlagAsync(challenge, context.TeamId, cancellationToken);
        var isCorrect = IsMatch(context.FlagContent, expectedFlag);

        if (!isCorrect && LooksLikeLegacyFullFlag(challenge.FlagSecret ?? string.Empty))
            isCorrect = IsMatch(context.FlagContent, challenge.FlagSecret ?? string.Empty);

        var hasPriorCorrectBreak = isCorrect && await HasCorrectBreakAsync(context, cancellationToken);
        var submission = new Submission
        {
            Id = Guid.NewGuid(),
            CompetitionId = context.CompetitionId,
            TeamId = context.TeamId,
            ChallengeId = context.ChallengeId,
            UserId = context.UserId,
            FlagContent = RedactSubmittedFlag(context.FlagContent),
            IsCorrect = isCorrect && !hasPriorCorrectBreak,
            SubmittedAt = now,
            IpAddress = context.IpAddress
        };
        db.Submissions.Add(submission);

        db.CompetitionLogs.Add(new CompetitionLog
        {
            Id = Guid.NewGuid(),
            CompetitionId = context.CompetitionId,
            Level = isCorrect ? "info" : "warning",
            EventType = isCorrect ? "awdp.break.success" : "awdp.break.failed",
            Message = isCorrect
                ? $"Team broke AWDP challenge {challenge.Title}; attack scoring starts at round settlement."
                : $"Team submitted an invalid AWDP break flag for challenge {challenge.Title}.",
            TeamId = context.TeamId,
            UserId = context.UserId,
            ChallengeId = context.ChallengeId,
            MetadataJson = ScoringJson.Serialize(new
            {
                submissionId = submission.Id,
                attackAttempts = state.AttackAttempts,
                maxAttackAttempts = config.MaxAttackAttempts
            }),
            CreatedAt = now
        });

        if (isCorrect)
        {
            state.BreakStatus = AwdpBreakStatus.BreakSuccess;
            state.BreakSucceededAt ??= now;
        }
        else
        {
            state.BreakStatus = state.AttackAttempts >= config.MaxAttackAttempts
                ? AwdpBreakStatus.AttackAttemptsExhausted
                : AwdpBreakStatus.BreakFailed;
        }

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            if (!await HasCorrectBreakAsync(context, cancellationToken))
                throw;

            return SubmissionResult.AlreadySolved;
        }

        return isCorrect ? SubmissionResult.Accepted : SubmissionResult.WrongFlag;
    }

    private async Task<bool> HasCorrectBreakAsync(SubmissionContext context, CancellationToken ct)
        => await db.Submissions
            .IgnoreQueryFilters()
            .AsNoTracking()
            .AnyAsync(s =>
                s.CompetitionId == context.CompetitionId &&
                s.TeamId == context.TeamId &&
                s.ChallengeId == context.ChallengeId &&
                s.IsCorrect, ct);

    private async Task<string> ResolveExpectedFlagAsync(Challenge challenge, Guid teamId, CancellationToken ct)
    {
        var flagSecret = challenge.FlagSecret ?? string.Empty;
        if (!string.Equals(flagSecret.Trim(), "[UUID]", StringComparison.OrdinalIgnoreCase))
            return FormatFlag(challenge, flagSecret);

        var flag = await db.CtfDynamicFlags
            .IgnoreQueryFilters()
            .AsNoTracking()
            .FirstOrDefaultAsync(f =>
                f.CompetitionId == challenge.CompetitionId &&
                f.TeamId == teamId &&
                f.ChallengeId == challenge.Id, ct);

        return flag is null ? string.Empty : FormatFlag(challenge, flag.FlagUuid);
    }

    private static string FormatFlag(Challenge challenge, string content)
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

    private static bool LooksLikeLegacyFullFlag(string value)
        => value.Contains('{', StringComparison.Ordinal) && value.EndsWith("}", StringComparison.Ordinal);

    private static bool IsMatch(string submitted, string expected)
    {
        var submittedBytes = Encoding.UTF8.GetBytes(submitted.Trim());
        var expectedBytes = Encoding.UTF8.GetBytes(expected.Trim());
        return submittedBytes.Length == expectedBytes.Length &&
               CryptographicOperations.FixedTimeEquals(submittedBytes, expectedBytes);
    }

    private static string RedactSubmittedFlag(string submittedFlag)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(submittedFlag));
        return $"sha256:{Convert.ToHexString(bytes).ToLowerInvariant()};len:{submittedFlag.Length}";
    }
}
