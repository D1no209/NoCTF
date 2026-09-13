using System.Net;
using System.Net.Http.Json;
using FastEndpoints;
using FluentStorage.Storage;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using NoCTF.API.Composition;
using NoCTF.API.Endpoints.Platform;
using NoCTF.Application.Administration.PlatformConfiguration;
using NoCTF.Application.Admission;
using NoCTF.Application.Storage;
using NoCTF.Domain.Platform;
using NSubstitute;

namespace NoCTF.Tests.Unit.API;

public sealed class PublicPlatformConfigurationEndpointTests
{
    [Test]
    [Arguments(true, true, true, HumanVerificationProviderProtocol.Cap, true, true)]
    [Arguments(true, false, true, HumanVerificationProviderProtocol.Cap, false, true)]
    [Arguments(true, true, false, HumanVerificationProviderProtocol.Cap, true, false)]
    [Arguments(false, true, true, HumanVerificationProviderProtocol.None, false, false)]
    public async Task Public_configuration_exposes_only_enabled_client_capabilities_without_provider_secrets(
        bool humanVerificationEnabled,
        bool runtimeEnabled,
        bool evaluationEnabled,
        HumanVerificationProviderProtocol expectedProvider,
        bool expectedRuntimeRequired,
        bool expectedEvaluationRequired)
    {
        var now = DateTimeOffset.UtcNow;
        var settings = Substitute.For<IPlatformConfigurationStore>();
        settings.GetAsync(Arg.Any<CancellationToken>())
            .Returns(new PlatformConfigurationView(
                "NoCTF", "Arena", null, now));
        var humanVerification = Substitute.For<IHumanVerificationConfigurationStore>();
        humanVerification.GetAsync(Arg.Any<CancellationToken>()).Returns(
            new HumanVerificationConfigurationView(
                humanVerificationEnabled,
                HumanVerificationProvider.Cap,
                true,
                "https://cap.example.test/root",
                "site-key",
                true,
                string.Empty,
                false,
                [],
                now,
                runtimeEnabled,
                evaluationEnabled));
        var objects = Substitute.For<IStore>();
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddFastEndpoints(options =>
        {
            options.DisableAutoDiscovery = true;
            options.Assemblies = [typeof(GetPublicPlatformConfigurationEndpoint).Assembly];
            options.Filter = type => type == typeof(GetPublicPlatformConfigurationEndpoint);
        });
        builder.Services.AddSingleton(settings);
        builder.Services.AddSingleton(humanVerification);
        builder.Services.AddSingleton(objects);
        builder.Services.AddSingleton(new FileUploadLimits(1_234_567, 7_654_321, 11, 12, 13));
        builder.Services.AddSingleton(new HumanVerificationValidationPolicy(true));
        builder.Services.AddSingleton(new ManagedFileUploads(
            Substitute.For<IManagedFileUploadRegistry>(),
            objects));
        builder.Services.AddScoped<ManagePlatformConfiguration>();
        builder.Services.AddScoped<ManageHumanVerificationConfiguration>();
        await using var app = builder.Build();
        app.UseNoCtfEndpoints();
        await app.StartAsync();
        using var client = app.GetTestClient();

        using var response = await client.GetAsync("/api/v1/platform/configuration");
        var content = await response.Content.ReadAsStringAsync();
        var payload = await response.Content.ReadFromJsonAsync<PublicPlatformConfigurationResponse>();

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(payload).IsNotNull();
        await Assert.That(payload!.ImageUploadLimits.MaximumAvatarBytes).IsEqualTo(1_234_567);
        await Assert.That(payload.ImageUploadLimits.MaximumWallpaperBytes).IsEqualTo(7_654_321);
        await Assert.That(payload.HumanVerification.Provider).IsEqualTo(expectedProvider);
        await Assert.That(payload.HumanVerification.SiteKey)
            .IsEqualTo(humanVerificationEnabled ? "site-key" : null);
        await Assert.That(payload.HumanVerification.ApiEndpoint)
            .IsEqualTo(humanVerificationEnabled
                ? "https://cap.example.test/root/site-key/"
                : null);
        await Assert.That(payload.HumanVerification.RuntimeRequired)
            .IsEqualTo(expectedRuntimeRequired);
        await Assert.That(payload.HumanVerification.EvaluationRequired)
            .IsEqualTo(expectedEvaluationRequired);
        await Assert.That(content).DoesNotContain("maximumLogoBytes");
        await Assert.That(content).DoesNotContain("maximumPosterBytes");
        await Assert.That(content).DoesNotContain("maximumAttachmentBytes");
        await Assert.That(content).DoesNotContain("objectKey");
        await Assert.That(content).DoesNotContain("provider-secret");
    }
}
