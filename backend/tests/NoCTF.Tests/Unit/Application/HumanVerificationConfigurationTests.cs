using NoCTF.Application.Admission;
using NoCTF.Domain.Platform;
using NSubstitute;

namespace NoCTF.Tests.Unit.Application;

public sealed class HumanVerificationConfigurationTests
{
    [Test]
    public async Task Enabling_cap_requires_https_site_key_and_a_configured_secret()
    {
        var store = Substitute.For<IHumanVerificationConfigurationStore>();
        store.GetAsync(Arg.Any<CancellationToken>()).Returns(Configuration(
            provider: HumanVerificationProvider.Cap,
            capSecretConfigured: false));
        var manager = new ManageHumanVerificationConfiguration(
            store,
            new HumanVerificationValidationPolicy(Development: false));

        var result = await manager.UpdateAsync(new(
            true,
            HumanVerificationProvider.Cap,
            "http://cap.example.test",
            "site-key",
            string.Empty,
            [],
            DateTimeOffset.UtcNow));

        await Assert.That(result.State)
            .IsEqualTo(HumanVerificationConfigurationUpdateState.Invalid);
        await Assert.That(result.Errors)
            .Contains(HumanVerificationConfigurationError.CapServerUrlInvalid);
        await Assert.That(result.Errors)
            .Contains(HumanVerificationConfigurationError.CapSecretRequired);
        await store.DidNotReceiveWithAnyArgs().UpdateAsync(default!, default);
    }

    [Test]
    public async Task Turnstile_update_normalizes_and_deduplicates_hostnames()
    {
        var store = Substitute.For<IHumanVerificationConfigurationStore>();
        store.GetAsync(Arg.Any<CancellationToken>()).Returns(Configuration(
            provider: HumanVerificationProvider.Turnstile,
            turnstileSecretConfigured: true));
        UpdateHumanVerificationConfigurationCommand? saved = null;
        store.UpdateAsync(
                Arg.Do<UpdateHumanVerificationConfigurationCommand>(value => saved = value),
                Arg.Any<CancellationToken>())
            .Returns(call => Configuration(
                enabled: call.ArgAt<UpdateHumanVerificationConfigurationCommand>(0).Enabled,
                provider: call.ArgAt<UpdateHumanVerificationConfigurationCommand>(0).Provider,
                turnstileSecretConfigured: true));
        var manager = new ManageHumanVerificationConfiguration(
            store,
            new HumanVerificationValidationPolicy(Development: false));

        var result = await manager.UpdateAsync(new(
            true,
            HumanVerificationProvider.Turnstile,
            string.Empty,
            string.Empty,
            " site-key ",
            ["CTF.Example.Test.", "ctf.example.test"],
            DateTimeOffset.UtcNow,
            RuntimeEnabled: false));

        await Assert.That(result.State)
            .IsEqualTo(HumanVerificationConfigurationUpdateState.Updated);
        await Assert.That(saved).IsNotNull();
        await Assert.That(saved!.TurnstileSiteKey).IsEqualTo("site-key");
        await Assert.That(saved.TurnstileAllowedHostnames)
            .IsEquivalentTo(["ctf.example.test"]);
        await Assert.That(saved.RuntimeEnabled).IsFalse();
    }

    [Test]
    public async Task None_is_an_explicit_disabled_provider()
    {
        var store = Substitute.For<IHumanVerificationConfigurationStore>();
        store.GetAsync(Arg.Any<CancellationToken>()).Returns(Configuration());
        store.UpdateAsync(
                Arg.Any<UpdateHumanVerificationConfigurationCommand>(),
                Arg.Any<CancellationToken>())
            .Returns(call => Configuration(
                enabled: call.ArgAt<UpdateHumanVerificationConfigurationCommand>(0).Enabled,
                provider: call.ArgAt<UpdateHumanVerificationConfigurationCommand>(0).Provider));
        var manager = new ManageHumanVerificationConfiguration(
            store,
            new HumanVerificationValidationPolicy(Development: false));

        var result = await manager.UpdateAsync(new(
            true,
            HumanVerificationProvider.None,
            string.Empty,
            string.Empty,
            string.Empty,
            [],
            DateTimeOffset.UtcNow));

        await Assert.That(result.State)
            .IsEqualTo(HumanVerificationConfigurationUpdateState.Updated);
        await store.Received(1).UpdateAsync(
            Arg.Is<UpdateHumanVerificationConfigurationCommand>(command =>
                command != null && !command.Enabled),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task Readiness_ignores_incomplete_settings_for_the_inactive_provider()
    {
        var store = Substitute.For<IHumanVerificationConfigurationStore>();
        store.GetAsync(Arg.Any<CancellationToken>()).Returns(new HumanVerificationConfigurationView(
            true,
            HumanVerificationProvider.Cap,
            false,
            "https://cap.example.test",
            "site-key",
            true,
            string.Empty,
            false,
            [string.Empty],
            DateTimeOffset.UnixEpoch));
        var manager = new ManageHumanVerificationConfiguration(
            store,
            new HumanVerificationValidationPolicy(Development: false));

        var configuration = await manager.GetAsync();

        await Assert.That(configuration.Ready).IsTrue();
    }

    private static HumanVerificationConfigurationView Configuration(
        bool enabled = false,
        HumanVerificationProvider provider = HumanVerificationProvider.None,
        bool capSecretConfigured = false,
        bool turnstileSecretConfigured = false) => new(
        enabled,
        provider,
        false,
        provider == HumanVerificationProvider.Cap
            ? "https://cap.example.test"
            : string.Empty,
        provider == HumanVerificationProvider.Cap ? "site-key" : string.Empty,
        capSecretConfigured,
        provider == HumanVerificationProvider.Turnstile ? "site-key" : string.Empty,
        turnstileSecretConfigured,
        provider == HumanVerificationProvider.Turnstile
            ? ["ctf.example.test"]
            : [],
        DateTimeOffset.UnixEpoch);
}
