using System.Security.Cryptography;
using System.Text;

namespace NoCTF.Application.Challenges.Flags;

public interface IPerTeamRuntimeFlagStore
{
    Task<string> EnsureAsync(
        Guid competitionId,
        Guid competitionChallengeId,
        Guid teamId,
        DateTimeOffset now,
        CancellationToken cancellationToken);

    Task<string> EnsureRuntimeInstanceAsync(
        Guid competitionId,
        Guid competitionChallengeId,
        Guid teamId,
        Guid runtimeInstanceId,
        DateTimeOffset now,
        CancellationToken cancellationToken);

    Task InvalidateRuntimeInstanceAsync(
        Guid runtimeInstanceId,
        DateTimeOffset now,
        CancellationToken cancellationToken);
}

public static class PerTeamRuntimeFlagDerivation
{
    public static string Derive(
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
