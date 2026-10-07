using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NSubstitute;
using NoCTF.Application.Authentication.Passkeys;
using NoCTF.Domain.Identity;
using NoCTF.Infrastructure.Authentication.Passkeys;

namespace NoCTF.Tests.Unit.Authentication;

public sealed class PasskeyProtocolTests
{
    [Test]
    public async Task Native_verifier_validates_registration_and_discoverable_assertion()
    {
        using var authenticator = new SoftwarePasskeyAuthenticator();
        var user = new User { Id = Guid.NewGuid(), UserName = "user", Kind = UserKind.Human };
        using var services = Services(user, out var store);
        var protocol = services.GetRequiredService<IPasskeyProtocol>();
        var registration = await protocol.CreateRegistrationOptionsAsync(new(user.Id, user.UserName), "https://noctf.test", CancellationToken.None);
        var result = await protocol.VerifyRegistrationAsync(registration.State, authenticator.Register(registration.OptionsJson, "https://noctf.test"), "https://noctf.test", CancellationToken.None);
        await Assert.That(result.Succeeded).IsTrue(); await Assert.That(result.Value!.UserId).IsEqualTo(user.Id);
        var credential = result.Value.Credential;
        store.FindPasskeyAsync(user, Arg.Any<byte[]>(), Arg.Any<CancellationToken>()).Returns(new UserPasskeyInfo(credential.CredentialId, credential.PublicKey, DateTimeOffset.UtcNow, 0, ["internal"], true, false, false, credential.AttestationObject, credential.ClientDataJson));
        var login = await protocol.CreateLoginOptionsAsync("https://noctf.test", CancellationToken.None);
        var asserted = await protocol.VerifyLoginAsync(login.State, authenticator.Assert(login.OptionsJson, "https://noctf.test"), "https://noctf.test", CancellationToken.None);
        await Assert.That(asserted.Succeeded).IsTrue(); await Assert.That(asserted.Value!.Credential.SignCount).IsEqualTo(1u);
    }
    [Test]
    [Arguments("https://evil.test", true)]
    [Arguments("https://child.noctf.test", true)]
    [Arguments("https://noctf.test", false)]
    public async Task Registration_rejects_wrong_origin_subdomains_and_missing_user_verification(string origin, bool verified)
    {
        using var authenticator = new SoftwarePasskeyAuthenticator(); var user = new User { Id = Guid.NewGuid(), UserName = "user" };
        using var services = Services(user, out _); var protocol = services.GetRequiredService<IPasskeyProtocol>();
        var options = await protocol.CreateRegistrationOptionsAsync(new(user.Id, user.UserName), "https://noctf.test", CancellationToken.None);
        var result = await protocol.VerifyRegistrationAsync(options.State, authenticator.Register(options.OptionsJson, origin, verified), "https://noctf.test", CancellationToken.None);
        await Assert.That(result.Succeeded).IsFalse();
    }
    [Test]
    [Arguments(true, false, false, 0u)]
    [Arguments(false, true, false, 0u)]
    [Arguments(false, false, true, 0u)]
    [Arguments(false, false, false, 1u)]
    public async Task Assertion_rejects_modified_challenge_signature_missing_UV_and_nonincreasing_counter(bool wrongChallenge, bool badSignature, bool missingUv, uint previousCount)
    {
        using var authenticator = new SoftwarePasskeyAuthenticator(); var user = new User { Id = Guid.NewGuid(), UserName = "user", Kind = UserKind.Human };
        using var services = Services(user, out var store); var protocol = services.GetRequiredService<IPasskeyProtocol>();
        var registration = await protocol.CreateRegistrationOptionsAsync(new(user.Id, user.UserName), "https://noctf.test", CancellationToken.None);
        var created = await protocol.VerifyRegistrationAsync(registration.State, authenticator.Register(registration.OptionsJson, "https://noctf.test"), "https://noctf.test", CancellationToken.None);
        var credential = created.Value!.Credential;
        store.FindPasskeyAsync(user, Arg.Any<byte[]>(), Arg.Any<CancellationToken>()).Returns(new UserPasskeyInfo(credential.CredentialId, credential.PublicKey, DateTimeOffset.UtcNow, previousCount, ["internal"], true, false, false, credential.AttestationObject, credential.ClientDataJson));
        var login = await protocol.CreateLoginOptionsAsync("https://noctf.test", CancellationToken.None);
        var assertion = authenticator.Assert(login.OptionsJson, "https://noctf.test", verified: !missingUv,
            challengeOverride: wrongChallenge ? SoftwarePasskeyAuthenticator.Encode(new byte[32]) : null, corruptSignature: badSignature);
        await Assert.That((await protocol.VerifyLoginAsync(login.State, assertion, "https://noctf.test", CancellationToken.None)).Succeeded).IsFalse();
    }

    private static ServiceProvider Services(User user, out IUserPasskeyStore<User> store)
    {
        store = Substitute.For<IUserPasskeyStore<User>>();
        store.FindByIdAsync(user.Id.ToString("N"), Arg.Any<CancellationToken>()).Returns(user);
        store.GetPasskeysAsync(user, Arg.Any<CancellationToken>()).Returns(new List<UserPasskeyInfo>());
        var services = new ServiceCollection(); services.AddLogging(); services.AddIdentityCore<User>();
        services.AddSingleton<IUserStore<User>>(store);
        services.Configure<IdentityPasskeyOptions>(options =>
        {
            options.ServerDomain = "noctf.test"; options.UserVerificationRequirement = "required";
            options.ValidateOrigin = context => ValueTask.FromResult(!context.CrossOrigin && context.TopOrigin is null && context.Origin == "https://noctf.test");
        });
        services.AddScoped<IPasskeyHandler<User>, PasskeyHandler<User>>(); services.AddScoped<IPasskeyProtocol, PasskeyProtocol>();
        return services.BuildServiceProvider();
    }
}
