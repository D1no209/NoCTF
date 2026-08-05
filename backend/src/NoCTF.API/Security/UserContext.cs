using System.Security.Claims;

namespace NoCTF.API.Security;

public interface IUserContext
{
    Guid UserId { get; }
    bool IsAdministrator { get; }
    bool IsHuman => true;
}

public sealed class HttpUserContext(IHttpContextAccessor accessor) : IUserContext
{
    public Guid UserId =>
        Guid.TryParse(accessor.HttpContext?.User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? accessor.HttpContext?.User.FindFirstValue("sub"), out var userId)
            ? userId
            : Guid.Empty;

    public bool IsAdministrator =>
        accessor.HttpContext?.User.IsInRole("Administrator") == true;

    public bool IsHuman => string.Equals(
        accessor.HttpContext?.User.FindFirstValue("user_kind"),
        "Human",
        StringComparison.Ordinal);
}
