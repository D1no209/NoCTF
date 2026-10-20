using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.OutputCaching;
using Microsoft.Extensions.Primitives;
using NoCTF.API.Security;
using NoCTF.Application.Scoring.Leaderboard;

namespace NoCTF.API.Endpoints.Competitions;

/// <summary>
/// Shares encoded leaderboard responses between every viewer of the same audience. The viewer's audience
/// (visibility, data scope, staff or participant track set, and a private own-team row when that track is
/// not public) is resolved before lookup, so the cache never mixes data between audiences. Entries are keyed
/// by publication version: a new publication produces a new key and old entries simply expire.
/// Concurrent misses for the same key are coalesced, so a publication burst renders once per audience.
/// </summary>
public sealed class LeaderboardOutputCachePolicy : IOutputCachePolicy
{
    public const string Name = "Leaderboard";
    private static readonly object ResponseVersion = new();
    private static readonly object PreparedAudience = new();

    private sealed record Prepared(Guid CompetitionId, ScoreboardAudience Audience);

    internal static ScoreboardAudience? GetPrepared(HttpContext context, Guid competitionId) =>
        context.Items.TryGetValue(PreparedAudience, out var stored)
            && stored is Prepared prepared
            && prepared.CompetitionId == competitionId
                ? prepared.Audience
                : null;

    public static void SetResponseVersion(HttpContext context, long version) => context.Items[ResponseVersion] = version;

    /// <summary>Strong validator for one publication as seen by one audience and round window.</summary>
    public static string EntityTag(long version, string cacheScope, int? endingRound)
    {
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes($"{cacheScope}|{endingRound}"));
        return $"\"{version.ToString(CultureInfo.InvariantCulture)}-{Convert.ToHexStringLower(digest.AsSpan(0, 8))}\"";
    }

    public async ValueTask CacheRequestAsync(OutputCacheContext context, CancellationToken ct)
    {
        context.EnableOutputCaching = true;
        context.AllowCacheLookup = false;
        context.AllowCacheStorage = false;
        context.AllowLocking = false;
        var http = context.HttpContext;
        if (!HttpMethods.IsGet(http.Request.Method)
            || !Guid.TryParse(http.Request.RouteValues["competitionId"]?.ToString(), out var competitionId))
            return;
        int? endingRound = null;
        if (http.Request.Query.TryGetValue("endingRound", out var rawRound) && !StringValues.IsNullOrEmpty(rawRound))
        {
            // Invalid or non-positive values are rejected by the endpoint validator without caching.
            if (rawRound.Count != 1 || !int.TryParse(rawRound[0], NumberStyles.None, CultureInfo.InvariantCulture, out var parsed)
                || parsed <= 0)
                return;
            endingRound = parsed;
        }

        var services = http.RequestServices;
        var user = services.GetRequiredService<IUserContext>();
        var audience = await services.GetRequiredService<ReadScoreboardAudience>()
            .ExecuteAsync(competitionId, user.UserId, endingRound, ct);
        http.Items[PreparedAudience] = new Prepared(competitionId, audience);
        if (audience is not { State: ScoreboardAudienceState.Ready, Projection: { } projection, CacheScope: { } scope })
            return;

        context.AllowCacheLookup = true;
        context.AllowCacheStorage = true;
        context.AllowLocking = true;
        context.ResponseExpirationTimeSpan = TimeSpan.FromSeconds(30);
        context.CacheVaryByRules.QueryKeys = "*";
        context.CacheVaryByRules.HeaderNames = "Accept-Encoding";
        context.CacheVaryByRules.VaryByValues["publication"] =
            projection.Snapshot.Version.ToString(CultureInfo.InvariantCulture);
        context.CacheVaryByRules.VaryByValues["audience"] = scope;
    }

    public ValueTask ServeFromCacheAsync(OutputCacheContext context, CancellationToken ct) => ValueTask.CompletedTask;

    public ValueTask ServeResponseAsync(OutputCacheContext context, CancellationToken ct)
    {
        context.CacheVaryByRules.VaryByValues.TryGetValue("publication", out var expected);
        if (context.HttpContext.Response.StatusCode != StatusCodes.Status200OK
            || !StringValues.IsNullOrEmpty(context.HttpContext.Response.Headers.SetCookie)
            || !context.HttpContext.Items.TryGetValue(ResponseVersion, out var version)
            || version is not long published
            || expected != published.ToString(CultureInfo.InvariantCulture))
            context.AllowCacheStorage = false;
        return ValueTask.CompletedTask;
    }
}
