using FastEndpoints;
using NoCTF.API.Permissions;

namespace NoCTF.API.Endpoints;

/// <summary>
/// Pre-processor that checks competition management permissions.
/// </summary>
public class CompetitionManagePreProcessor<TRequest> : IPreProcessor<TRequest>
    where TRequest : ICompetitionRequest
{
    public async Task PreProcessAsync(IPreProcessorContext<TRequest> ctx, CancellationToken ct)
    {
        var permissionService = ctx.HttpContext.Resolve<ICompetitionPermissionService>();
        var userIdClaim = ctx.HttpContext.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        if (!Guid.TryParse(userIdClaim, out var userId))
        {
            await ctx.HttpContext.Response.SendForbiddenAsync(ct);
            return;
        }

        var canManage = await permissionService.CanManageCompetitionAsync(userId, ctx.Request!.CompetitionId, ct);
        if (!canManage)
        {
            await ctx.HttpContext.Response.SendForbiddenAsync(ct);
        }
    }
}

public interface ICompetitionRequest
{
    Guid CompetitionId { get; }
}
