namespace NoCTF.Application.Submissions.Ports;

/// <summary>Reads the current dynamic flag for the submitting team's challenge instance.</summary>
public interface IChallengeInstanceFlagReader
{
    Task<string?> ReadAsync(
        Guid competitionId,
        Guid teamId,
        Guid challengeId,
        DateTimeOffset asOf,
        CancellationToken cancellationToken);
}
