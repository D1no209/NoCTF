using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using FastEndpoints;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Configuration;
using NSubstitute;
using NoCTF.API.Composition;
using NoCTF.API.Endpoints.GameplayFacts;
using NoCTF.API.Security;

namespace NoCTF.Tests.Unit.API;

public sealed class SubmissionRateLimitingHttpTests
{
    private const string BearerScheme = "Bearer";
    private static readonly Guid ActorId = Guid.Parse(
        "bc05abda-f12e-4814-89b4-a4076010181b");

    [Test]
    public async Task Submission_policy_partitions_authenticated_users_and_rejects_excess_with_429()
    {
        const int configuredPermitLimit = 3;
        await using var app = await CreateApplicationAsync(configuredPermitLimit);
        using var client = app.GetTestClient();
        var firstUserId = Guid.NewGuid();
        var secondUserId = Guid.NewGuid();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue(BearerScheme, firstUserId.ToString());
        var route =
            $"/api/v1/competitions/{Guid.NewGuid()}/challenges/{Guid.NewGuid()}/flag-submissions";

        for (var attempt = 0; attempt < configuredPermitLimit; attempt++)
        {
            using var acceptedByLimiter = await client.PostAsJsonAsync(
                route,
                new { flag = "FLAG" });
            await Assert.That(acceptedByLimiter.StatusCode)
                .IsNotEqualTo(HttpStatusCode.TooManyRequests);
        }

        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue(BearerScheme, secondUserId.ToString());
        using var otherUser = await client.PostAsJsonAsync(
            route,
            new { flag = "FLAG" });
        await Assert.That(otherUser.StatusCode)
            .IsNotEqualTo(HttpStatusCode.TooManyRequests);

        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue(BearerScheme, firstUserId.ToString());
        using var rejected = await client.PostAsJsonAsync(
            route,
            new { flag = "FLAG" });
        await Assert.That(rejected.StatusCode)
            .IsEqualTo(HttpStatusCode.TooManyRequests);
        await Assert.That(await rejected.Content.ReadAsStringAsync()).Contains("RateLimited");
        await Assert.That(rejected.Headers.RetryAfter).IsNotNull();
    }

    private static async Task<WebApplication> CreateApplicationAsync(int submissionPerUserPerMinute)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["RequestAdmission:SubmissionPerUserPerMinute"] =
                submissionPerUserPerMinute.ToString(System.Globalization.CultureInfo.InvariantCulture)
        });
        builder.Services.AddNoCtfApi(
            builder.Configuration,
            includeInfrastructure: false);
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddFastEndpoints(options =>
        {
            options.DisableAutoDiscovery = true;
            options.Assemblies = [typeof(SubmitFlagEndpoint).Assembly];
            options.Filter = type =>
                type == typeof(SubmitFlagEndpoint)
                || type == typeof(SubmitFlagRequestValidator);
        });
        builder.Services
            .AddAuthentication(options =>
            {
                options.DefaultAuthenticateScheme = BearerScheme;
                options.DefaultChallengeScheme = BearerScheme;
            })
            .AddScheme<AuthenticationSchemeOptions, TestBearerHandler>(
                BearerScheme,
                _ => { });
        builder.Services.AddAuthorization();

        var user = Substitute.For<IUserContext>();
        user.UserId.Returns(ActorId);
        builder.Services.AddSingleton(user);

        var app = builder.Build();
        app.UseNoCtfPipeline();
        app.UseNoCtfEndpoints();
        await app.StartAsync();
        return app;
    }

    private sealed class TestBearerHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(
            options,
            logger,
            encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var subject = Context.Request.Headers.Authorization
                .ToString()
                .Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .LastOrDefault();
            if (!Guid.TryParse(subject, out var subjectId))
                return Task.FromResult(AuthenticateResult.Fail("Invalid test subject."));
            var identity = new ClaimsIdentity(
                [
                    new Claim(ClaimTypes.NameIdentifier, subjectId.ToString())
                ],
                Scheme.Name);
            var ticket = new AuthenticationTicket(
                new ClaimsPrincipal(identity),
                Scheme.Name);
            return Task.FromResult(AuthenticateResult.Success(ticket));
        }
    }
}
