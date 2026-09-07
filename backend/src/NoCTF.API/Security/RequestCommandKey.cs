using NoCTF.Application.Commands.Idempotency;

namespace NoCTF.API.Security;

public sealed class RequestCommandKey(IHttpContextAccessor context) : IRequestCommandKey
{
    public Guid? ActorId => Guid.TryParse(context.HttpContext?.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
        ?? context.HttpContext?.User.FindFirst("sub")?.Value, out var actor) ? actor : null;
    public Guid? Key => Guid.TryParse(context.HttpContext?.Request.Headers["Idempotency-Key"], out var key) && key != Guid.Empty ? key : null;
}
