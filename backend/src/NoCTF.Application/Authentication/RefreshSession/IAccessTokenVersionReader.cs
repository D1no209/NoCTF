namespace NoCTF.Application.Authentication.RefreshSession;

public sealed record LegacyAdministratorIssuedAccessToken(
    Guid JwtId,
    Guid AdministratorUserId);

public interface IAccessTokenVersionReader
{
    Task<bool> IsCurrentAsync(
        Guid userId,
        int tokenVersion,
        CancellationToken cancellationToken,
        LegacyAdministratorIssuedAccessToken? legacyAdministratorIssuedToken = null);
}
