using NoCTF.Application.Admission;
using NoCTF.Domain.Platform;
using NSubstitute;

namespace NoCTF.Tests.Unit.Application;

public sealed class CapWorkloadConfigurationTests
{
    [Test]
    public async Task Invalid_workload_is_rejected_before_contacting_cap()
    {
        var reader = Substitute.For<IHumanVerificationConfigurationReader>();
        var client = Substitute.For<ICapWorkloadConfigurationClient>();
        var manager = new ManageCapWorkloadConfiguration(reader, client);

        var result = await manager.UpdateAsync(9, 80);

        await Assert.That(result.Error)
            .IsEqualTo(CapWorkloadConfigurationError.DifficultyInvalid);
        await reader.DidNotReceiveWithAnyArgs()
            .GetRuntimeConfigurationAsync(default);
        await client.DidNotReceiveWithAnyArgs()
            .UpdateAsync(default!, default, default, default);
    }

    [Test]
    public async Task Non_cap_provider_is_rejected_without_network_access()
    {
        var reader = Substitute.For<IHumanVerificationConfigurationReader>();
        reader.GetRuntimeConfigurationAsync(Arg.Any<CancellationToken>())
            .Returns(new HumanVerificationRuntimeConfiguration(
                true,
                new HumanVerificationOptions
                {
                    Provider = HumanVerificationProvider.Turnstile
                }));
        var client = Substitute.For<ICapWorkloadConfigurationClient>();
        var manager = new ManageCapWorkloadConfiguration(reader, client);

        var result = await manager.GetAsync();

        await Assert.That(result.Error)
            .IsEqualTo(CapWorkloadConfigurationError.ProviderNotCap);
        await client.DidNotReceiveWithAnyArgs().GetAsync(default!, default);
    }

    [Test]
    public async Task Valid_workload_is_forwarded_to_cap_client()
    {
        var options = new HumanVerificationOptions
        {
            Provider = HumanVerificationProvider.Cap,
            Cap = new() { SiteKey = "site-key" }
        };
        var reader = Substitute.For<IHumanVerificationConfigurationReader>();
        reader.GetRuntimeConfigurationAsync(Arg.Any<CancellationToken>())
            .Returns(new HumanVerificationRuntimeConfiguration(true, options));
        var client = Substitute.For<ICapWorkloadConfigurationClient>();
        client.UpdateAsync(
                options.Cap,
                8,
                1,
                Arg.Any<CancellationToken>())
            .Returns(new CapWorkloadConfigurationResult(
                new CapWorkloadConfiguration(8, 1, 32)));
        var manager = new ManageCapWorkloadConfiguration(reader, client);

        var result = await manager.UpdateAsync(8, 1);

        await Assert.That(result.Succeeded).IsTrue();
        await Assert.That(result.Configuration!.ExpectedHashAttempts)
            .IsEqualTo(4_294_967_296L);
    }
}
