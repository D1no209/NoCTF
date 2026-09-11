namespace NoCTF.Application.Authentication.RefreshSession;

public sealed record AdministratorIssuedAccessToken(
    Guid JwtId,
    Guid AdministratorUserId);

public interface IAccessTokenVersionReader
{
    Task<bool> IsCurrentAsync(
        Guid userId,
        int tokenVersion,
        CancellationToken cancellationToken,
        AdministratorIssuedAccessToken? administratorIssuedToken = null);
}
