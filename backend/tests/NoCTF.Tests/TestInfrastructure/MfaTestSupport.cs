using NSubstitute;
using NoCTF.Application.Authentication.Account;
using NoCTF.Application.Authentication.Mfa;
using NoCTF.Domain.Identity;
using NoCTF.Domain.Identity.Mfa;

namespace NoCTF.Tests;

internal static class MfaTestSupport
{
    internal static IMfaAuthenticationStore Unrequired(AuthenticatedUser? user = null)
    {
        var store = Substitute.For<IMfaAuthenticationStore>();
        store.ReadAccountAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(call =>
            new MfaAccountSnapshot(user ?? new(call.Arg<Guid>(), "test", UserRole.User, UserKind.Human, 0),
                false, false, Guid.Empty, null, 0, false));
        return store;
    }
    internal static AuthenticationContext Primary(DateTimeOffset time, UserKind kind = UserKind.Human) =>
        new(kind == UserKind.Bot ? AuthenticationMethod.Bot : AuthenticationMethod.Password, DateTimeOffset.FromUnixTimeSeconds(time.ToUnixTimeSeconds()));
}
