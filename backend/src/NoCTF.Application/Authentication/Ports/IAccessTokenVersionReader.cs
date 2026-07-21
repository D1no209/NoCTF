namespace NoCTF.Application.Authentication.Ports;

public interface IAccessTokenVersionReader
{
    Task<bool> IsCurrentAsync(Guid userId, int tokenVersion, CancellationToken cancellationToken);
}
