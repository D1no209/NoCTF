using NoCTF.Application.Authentication.Ports;

namespace NoCTF.Application.Authentication.Logout;

public sealed class LogoutUser(IUserAuthenticationStore store)
{
    public Task ExecuteAsync(Guid familyId, DateTimeOffset now, CancellationToken cancellationToken = default) =>
        store.RevokeRefreshFamilyAsync(familyId, now, cancellationToken);
}
