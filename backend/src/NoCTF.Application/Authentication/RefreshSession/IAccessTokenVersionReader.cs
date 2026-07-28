namespace NoCTF.Application.Authentication.RefreshSession;

public interface IAccessTokenVersionReader
{
    Task<bool> IsCurrentAsync(Guid userId, int tokenVersion, CancellationToken cancellationToken);
}
