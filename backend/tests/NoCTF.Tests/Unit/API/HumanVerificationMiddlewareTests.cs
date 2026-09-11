using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.DependencyInjection;
using FluentStorage.Storage;
using NoCTF.API.Security;
using NoCTF.Application.Administration.PlatformConfiguration;
using NoCTF.Application.Admission;
using NoCTF.Application.Authentication.Privacy;
using NoCTF.Application.Storage;
using NSubstitute;

namespace NoCTF.Tests.Unit.API;

public sealed class HumanVerificationMiddlewareTests
{
    private static readonly IServiceProvider RequestServices = new ServiceCollection()
        .AddLogging()
        .AddProblemDetails()
        .BuildServiceProvider();

    [Test]
    public async Task Disabled_switch_none_provider_and_unmarked_endpoints_bypass_verification()
    {
        var verifier = Substitute.For<IHumanVerificationVerifier>();
        var source = Substitute.For<IRequestSourceAddress>();
        var calls = 0;
        var middleware = new HumanVerificationMiddleware(_ =>
        {
            calls++;
            return Task.CompletedTask;
        });

        var disabled = Context(HumanVerificationAction.Login);
        await middleware.InvokeAsync(
            disabled,
            verifier,
            source,
            PlatformConfiguration(enabled: true),
            Options.Create(new HumanVerificationOptions()));

        var administrativelyDisabled = Context(HumanVerificationAction.Login);
        await middleware.InvokeAsync(
            administrativelyDisabled,
            verifier,
            source,
            PlatformConfiguration(enabled: false),
            Options.Create(TurnstileOptions()));

        var unmarked = Context(action: null);
        await middleware.InvokeAsync(
            unmarked,
            verifier,
            source,
            PlatformConfiguration(enabled: true),
            Options.Create(TurnstileOptions()));

        await Assert.That(calls).IsEqualTo(3);
        await verifier.DidNotReceiveWithAnyArgs().VerifyAsync(default!, default);
    }

    [Test]
    public async Task Missing_or_oversized_token_is_rejected_before_provider_call()
    {
        var verifier = Substitute.For<IHumanVerificationVerifier>();
        var source = Substitute.For<IRequestSourceAddress>();
        var middleware = new HumanVerificationMiddleware(_ => Task.CompletedTask);

        var missing = Context(HumanVerificationAction.Login);
        await middleware.InvokeAsync(
            missing,
            verifier,
            source,
            PlatformConfiguration(enabled: true),
            Options.Create(TurnstileOptions()));

        var oversized = Context(HumanVerificationAction.Login);
        oversized.Request.Headers[HumanVerificationDefaults.HeaderName] =
            new string('x', HumanVerificationDefaults.MaximumTokenLength + 1);
        await middleware.InvokeAsync(
            oversized,
            verifier,
            source,
            PlatformConfiguration(enabled: true),
            Options.Create(TurnstileOptions()));

        await Assert.That(missing.Response.StatusCode).IsEqualTo(StatusCodes.Status403Forbidden);
        await Assert.That(await ProblemCodeAsync(missing)).IsEqualTo("HumanVerificationRequired");
        await Assert.That(oversized.Response.StatusCode).IsEqualTo(StatusCodes.Status403Forbidden);
        await Assert.That(await ProblemCodeAsync(oversized)).IsEqualTo("HumanVerificationFailed");
        await verifier.DidNotReceiveWithAnyArgs().VerifyAsync(default!, default);
    }

    [Test]
    public async Task Verified_token_carries_action_and_normalized_source_to_provider()
    {
        var verifier = Substitute.For<IHumanVerificationVerifier>();
        verifier.VerifyAsync(Arg.Any<HumanVerificationAttempt>(), Arg.Any<CancellationToken>())
            .Returns(HumanVerificationResult.Verified);
        var source = Substitute.For<IRequestSourceAddress>();
        source.Address.Returns("192.0.2.20");
        var reachedEndpoint = false;
        var middleware = new HumanVerificationMiddleware(_ =>
        {
            reachedEndpoint = true;
            return Task.CompletedTask;
        });
        var context = Context(HumanVerificationAction.Runtime);
        context.Request.Headers[HumanVerificationDefaults.HeaderName] = "proof";

        await middleware.InvokeAsync(
            context,
            verifier,
            source,
            PlatformConfiguration(enabled: true),
            Options.Create(TurnstileOptions()));

        await Assert.That(reachedEndpoint).IsTrue();
        await verifier.Received(1).VerifyAsync(
            Arg.Is<HumanVerificationAttempt>(attempt =>
                attempt != null
                && attempt.Token == "proof"
                && attempt.Action == HumanVerificationAction.Runtime
                && attempt.RemoteIpAddress == "192.0.2.20"),
            context.RequestAborted);
    }

    [Test]
    [Arguments(HumanVerificationResult.Rejected, 403, "HumanVerificationFailed")]
    [Arguments(HumanVerificationResult.Unavailable, 503, "HumanVerificationUnavailable")]
    public async Task Provider_failures_use_stable_problem_codes(
        HumanVerificationResult result,
        int expectedStatus,
        string expectedCode)
    {
        var verifier = Substitute.For<IHumanVerificationVerifier>();
        verifier.VerifyAsync(Arg.Any<HumanVerificationAttempt>(), Arg.Any<CancellationToken>())
            .Returns(result);
        var context = Context(HumanVerificationAction.Evaluation);
        context.Request.Headers[HumanVerificationDefaults.HeaderName] = "proof";

        await new HumanVerificationMiddleware(_ => Task.CompletedTask).InvokeAsync(
            context,
            verifier,
            Substitute.For<IRequestSourceAddress>(),
            PlatformConfiguration(enabled: true),
            Options.Create(TurnstileOptions()));

        await Assert.That(context.Response.StatusCode).IsEqualTo(expectedStatus);
        await Assert.That(await ProblemCodeAsync(context)).IsEqualTo(expectedCode);
        await Assert.That(context.Response.Headers.RetryAfter.ToString())
            .IsEqualTo(result == HumanVerificationResult.Unavailable ? "5" : string.Empty);
    }

    private static DefaultHttpContext Context(HumanVerificationAction? action)
    {
        var context = new DefaultHttpContext();
        context.RequestServices = RequestServices;
        context.Response.Body = new MemoryStream();
        if (action is not null)
        {
            context.SetEndpoint(new Endpoint(
                _ => Task.CompletedTask,
                new EndpointMetadataCollection(new HumanVerificationMetadata(action.Value)),
                "test"));
        }
        return context;
    }

    private static HumanVerificationOptions TurnstileOptions() => new()
    {
        Provider = HumanVerificationProvider.Turnstile,
        Turnstile = new TurnstileHumanVerificationOptions
        {
            SiteKey = "site-key",
            Secret = "secret",
            AllowedHostnames = ["ctf.example.test"]
        }
    };

    private static ManagePlatformConfiguration PlatformConfiguration(bool enabled)
    {
        var store = Substitute.For<IPlatformConfigurationStore>();
        store.GetAsync(Arg.Any<CancellationToken>()).Returns(new PlatformConfigurationView(
            "NoCTF", null, null, enabled, DateTimeOffset.UnixEpoch));
        var objects = Substitute.For<IStore>();
        return new ManagePlatformConfiguration(
            store,
            objects,
            new ManagedFileUploads(
                Substitute.For<IManagedFileUploadRegistry>(),
                objects));
    }

    private static async Task<string?> ProblemCodeAsync(DefaultHttpContext context)
    {
        context.Response.Body.Position = 0;
        using var problem = await JsonDocument.ParseAsync(context.Response.Body);
        return problem.RootElement.GetProperty("code").GetString();
    }
}
