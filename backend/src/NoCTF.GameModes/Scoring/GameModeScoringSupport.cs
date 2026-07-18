using System.Security.Cryptography;
using System.Text;
using NoCTF.Application.Scoring.Evaluation;

namespace NoCTF.GameModes.Scoring;

internal static class GameModeScoringSupport
{
    public static bool IsEligible(ScoringContext context, Guid teamId, Guid challengeId) =>
        context.Teams.TryGetValue(teamId, out var team) && !team.IsBanned && !team.IsDeleted
        && context.Challenges.TryGetValue(challengeId, out var challenge) && !challenge.IsDeleted;

    public static Guid StableId(string idempotencyKey) =>
        new(SHA256.HashData(Encoding.UTF8.GetBytes(idempotencyKey)).AsSpan(0, 16));
}
