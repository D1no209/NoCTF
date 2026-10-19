using System.Globalization;
using Microsoft.AspNetCore.OutputCaching;
using Microsoft.Extensions.Primitives;
using NoCTF.Application.Competitions.Visibility;
using NoCTF.Application.Scoring.Leaderboard;
using NoCTF.Domain.Competitions;

namespace NoCTF.API.Endpoints.Competitions;

public sealed class PublicCtfLeaderboardCachePolicy : IOutputCachePolicy
{
    public const string Name = "PublicCtfLeaderboard";
    private static readonly object ResponseVersion = new();
    private static readonly object PreparedSelection = new();

    internal static (CompetitionVisibilityAccessDecision Permission, ScoreboardProjection Projection)? GetPrepared(HttpContext context, Guid id) =>
        context.Items.TryGetValue(PreparedSelection, out var stored)
            && stored is ValueTuple<CompetitionVisibilityAccessDecision, ScoreboardProjection> selection
            && selection.Item2.Snapshot.CompetitionId == id ? selection : null;

    public static void SetResponseVersion(HttpContext context, long version) => context.Items[ResponseVersion] = version;

    public async ValueTask CacheRequestAsync(OutputCacheContext context, CancellationToken ct)
    {
        context.EnableOutputCaching = true;
        context.AllowCacheLookup = false;
        context.AllowCacheStorage = false;
        context.AllowLocking = false;
        if (!HttpMethods.IsGet(context.HttpContext.Request.Method)
            || !Guid.TryParse(context.HttpContext.Request.RouteValues["competitionId"]?.ToString(), out var id)) return;
        var services = context.HttpContext.RequestServices;
        var access = services.GetRequiredService<ICompetitionVisibilityAccess>();
        var leaderboard = services.GetRequiredService<ILeaderboardCache>();
        var clock = services.GetRequiredService<TimeProvider>();
        var permission = await access.ResolveAsync(Guid.Empty, id, clock.GetUtcNow(), ct);
        if (permission is not { GameMode: GameMode.Ctf, Visibility: CompetitionLeaderboardVisibility.Normal, TracksEnabled: false }) return;
        var projection = await leaderboard.GetScoreboardAsync(id, ct);
        if (projection is null || projection.Snapshot.TracksEnabled || projection.Snapshot.Tracks.Any(track => track.IsInternal)) return;
        context.HttpContext.Items[PreparedSelection] = (permission, projection);
        context.AllowCacheLookup = true;
        context.AllowCacheStorage = true;
        context.ResponseExpirationTimeSpan = TimeSpan.FromSeconds(15);
        context.CacheVaryByRules.QueryKeys = "*";
        context.CacheVaryByRules.HeaderNames = "Accept-Encoding";
        context.CacheVaryByRules.VaryByValues["publication"] = projection.Snapshot.Version.ToString(CultureInfo.InvariantCulture);
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
