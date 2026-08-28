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
using NSubstitute;
using NoCTF.API.Composition;
using NoCTF.API.Endpoints.Challenges.Questions;
using NoCTF.API.Security;
using NoCTF.Application.Challenges.Questions;

namespace NoCTF.Tests.Unit.API;

public sealed class CompetitionQuestionRateLimitingHttpTests
{
    private const string BearerScheme = "Bearer";
    private static readonly Guid ActorId = Guid.Parse(
        "bc05abda-f12e-4814-89b4-a4076010181b");

    [Test]
    [Property("Category", "CompetitionQuestionRateLimit")]
    public async Task Question_policy_partitions_users_and_rejects_the_ninth_write_with_429()
    {
        await using var app = await CreateApplicationAsync();
        using var client = app.GetTestClient();
        var firstUserId = Guid.NewGuid();
        var secondUserId = Guid.NewGuid();
        var route = $"/api/v1/competitions/{Guid.NewGuid()}/questions";
        var request = new
        {
            subject = "Platform",
            title = "Runner access",
            body = "The assigned runtime cannot be reached."
        };

        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue(BearerScheme, firstUserId.ToString());
        for (var attempt = 0; attempt < 8; attempt++)
        {
            using var acceptedByLimiter = await client.PostAsJsonAsync(route, request);
            await Assert.That(acceptedByLimiter.StatusCode)
                .IsNotEqualTo(HttpStatusCode.TooManyRequests);
        }

        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue(BearerScheme, secondUserId.ToString());
        using var otherUser = await client.PostAsJsonAsync(route, request);
        await Assert.That(otherUser.StatusCode)
            .IsNotEqualTo(HttpStatusCode.TooManyRequests);

        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue(BearerScheme, firstUserId.ToString());
        using var rejected = await client.PostAsJsonAsync(route, request);
        await Assert.That(rejected.StatusCode)
            .IsEqualTo(HttpStatusCode.TooManyRequests);
        await Assert.That(await rejected.Content.ReadAsByteArrayAsync())
            .IsEmpty();
    }

    private static async Task<WebApplication> CreateApplicationAsync()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddNoCtfApi(
            builder.Configuration,
            includeInfrastructure: false);
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddFastEndpoints(options =>
        {
            options.DisableAutoDiscovery = true;
            options.Assemblies = [typeof(CreateCompetitionQuestionEndpoint).Assembly];
            options.Filter = type =>
                type == typeof(CreateCompetitionQuestionEndpoint)
                || type == typeof(CreateCompetitionQuestionValidator);
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

        var store = Substitute.For<ICompetitionQuestionStore>();
        store.CreateAsync(
                Arg.Any<CreateCompetitionQuestionCommand>(),
                Arg.Any<CancellationToken>())
            .Returns(new CompetitionQuestionMutationResult(
                null,
                CompetitionQuestionFailure.NotFound));
        builder.Services.AddSingleton(store);
        builder.Services.AddSingleton<ICompetitionQuestionReader>(store);
        builder.Services.AddSingleton<ICompetitionQuestionWriter>(store);
        builder.Services.AddScoped<CreateCompetitionQuestion>();

        var user = Substitute.For<IUserContext>();
        user.UserId.Returns(ActorId);
        user.IsHuman.Returns(true);
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
                [new Claim(ClaimTypes.NameIdentifier, subjectId.ToString())],
                Scheme.Name);
            var ticket = new AuthenticationTicket(
                new ClaimsPrincipal(identity),
                Scheme.Name);
            return Task.FromResult(AuthenticateResult.Success(ticket));
        }
    }
}
