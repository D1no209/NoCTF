using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.Application.Competitions.Access;
using NoCTF.Application.Teams.Moderation;
using NoCTF.Domain.Competitions;

namespace NoCTF.API.Security;

public sealed class CompetitionAudienceGateMiddleware(RequestDelegate next)
{
    private static readonly PathString PublicPrefix = new("/api/v1/competitions");
    private static readonly PathString AdministrationPrefix = new("/api/v1/admin/competitions");
    private static readonly object AccessModeItemKey = new();

    public async Task InvokeAsync(
        HttpContext context,
        ICompetitionAudienceReader audiences,
        ICompetitionModerationAuthorizer authorizer,
        IUserContext user)
    {
        if (!AppliesTo(context.Request.Path)
            || !Guid.TryParse(
                context.Request.RouteValues["competitionId"]?.ToString(),
                out var competitionId))
        {
            await next(context);
            return;
        }

        var accessMode = await audiences.GetAccessModeAsync(
            competitionId,
            context.RequestAborted);
        if (accessMode != CompetitionAccessMode.StaffOnly)
        {
            await next(context);
            return;
        }

        context.Items[AccessModeItemKey] = accessMode;
        context.Response.Headers.CacheControl = "private,no-store";
        context.Response.Headers.Vary = "Authorization";
        if (user.UserId != Guid.Empty
            && await authorizer.CanObserveAsync(
                user.UserId,
                competitionId,
                context.RequestAborted))
        {
            await next(context);
            return;
        }

        await TypedResults.NotFound().ExecuteAsync(context);
    }

    public static bool IsStaffOnly(HttpContext context) =>
        context.Items.TryGetValue(AccessModeItemKey, out var value)
        && value is CompetitionAccessMode.StaffOnly;

    private static bool AppliesTo(PathString path) =>
        path.StartsWithSegments(PublicPrefix, StringComparison.OrdinalIgnoreCase)
        || path.StartsWithSegments(AdministrationPrefix, StringComparison.OrdinalIgnoreCase);
}
