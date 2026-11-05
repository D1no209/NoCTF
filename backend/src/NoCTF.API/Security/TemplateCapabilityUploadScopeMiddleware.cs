using System.Security.Claims;
using NoCTF.Application.Challenges.Testing;
using NoCTF.Domain.Challenges.Capabilities;

namespace NoCTF.API.Security;

public sealed class TemplateCapabilityUploadScopeMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context, ITemplateCapabilityTestStore tests)
    {
        var subject = context.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? context.User.FindFirstValue("sub");
        if (!Guid.TryParse(subject, out var actorId)
            || !Guid.TryParse(context.Request.RouteValues["challengeId"]?.ToString(), out var challengeId))
        {
            await TypedResults.NotFound().ExecuteAsync(context);
            return;
        }
        var access = await tests.ReadAsync(new(challengeId, actorId, context.User.IsInRole("Administrator")),
            ChallengeCapabilityKind.Fix, context.RequestAborted);
        if (access.FailureCode == TemplateCapabilityTestFailure.NotFound)
        {
            await TypedResults.NotFound().ExecuteAsync(context);
            return;
        }
        await next(context);
    }
}
