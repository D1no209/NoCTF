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
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using NoCTF.API.Composition;
using NoCTF.API.Endpoints.Challenges.Questions;
using NoCTF.API.Security;
using NoCTF.Application.Authentication.EmailVerification;
using NoCTF.Application.Challenges.Questions;
using NoCTF.Domain.Identity;

namespace NoCTF.Tests.Unit.API;

public sealed class CompetitionQuestionPaginationHttpTests
{
    private const string BearerScheme = "Bearer";
    private const string SigningKey =
        "competition-question-pagination-tests-use-a-stable-signing-key";

    [Test]
    public async Task List_cursor_is_typed_signed_and_bound_to_visibility_scope()
    {
        var store = Substitute.For<ICompetitionQuestionStore>();
        var anchor = new CompetitionQuestionPagePosition(
            DateTimeOffset.Parse("2026-08-11T10:00:00Z"),
            Guid.Parse("019feab0-0000-7000-8000-000000000001"));
        store.ListAsync(
                Arg.Any<CompetitionQuestionQuery>(),
                Arg.Any<CancellationToken>())
            .Returns(new CompetitionQuestionPage([], anchor));
        await using var app = await CreateApplicationAsync(store);
        using var client = app.GetTestClient();
        var actorId = Guid.Parse("019feab0-0000-7000-8000-000000000002");
        var otherActorId = Guid.Parse("019feab0-0000-7000-8000-000000000003");
        var competitionId = Guid.Parse("019feab0-0000-7000-8000-000000000004");
        var otherCompetitionId = Guid.Parse("019feab0-0000-7000-8000-000000000005");
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue(BearerScheme, actorId.ToString());
        var route = $"/api/v1/competitions/{competitionId}/questions?status=Pending&limit=20";

        using var first = await client.GetAsync(route);
        await Assert.That(first.StatusCode).IsEqualTo(HttpStatusCode.OK);
        var response = await first.Content.ReadFromJsonAsync<CompetitionQuestionListResponse>();
        await Assert.That(response).IsNotNull();
        await Assert.That(response!.NextCursor).IsNotNull();
        var cursor = Uri.EscapeDataString(response.NextCursor!);

        using var sameScope = await client.GetAsync($"{route}&cursor={cursor}");
        await Assert.That(sameScope.StatusCode).IsEqualTo(HttpStatusCode.OK);
        var positionedQuery = store.ReceivedCalls()
            .Select(call => call.GetArguments().FirstOrDefault())
            .OfType<CompetitionQuestionQuery>()
            .Last();
        await Assert.That(positionedQuery.Position).IsEqualTo(anchor);

        await AssertInvalidCursorAsync(
            client,
            $"/api/v1/competitions/{otherCompetitionId}/questions?status=Pending&limit=20&cursor={cursor}");
        await AssertInvalidCursorAsync(
            client,
            $"/api/v1/competitions/{competitionId}/questions?status=Closed&limit=20&cursor={cursor}");
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue(BearerScheme, otherActorId.ToString());
        await AssertInvalidCursorAsync(client, $"{route}&cursor={cursor}");
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue(BearerScheme, actorId.ToString());
        var tampered = response.NextCursor![0] == 'A'
            ? $"B{response.NextCursor[1..]}"
            : $"A{response.NextCursor[1..]}";
        await AssertInvalidCursorAsync(
            client,
            $"{route}&cursor={Uri.EscapeDataString(tampered)}");

        await Assert.That(store.ReceivedCalls().Count()).IsEqualTo(2);
    }

    private static async Task AssertInvalidCursorAsync(HttpClient client, string route)
    {
        using var response = await client.GetAsync(route);
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
        await Assert.That(response.Content.Headers.ContentType?.MediaType)
            .IsEqualTo("application/problem+json");
    }

    private static async Task<WebApplication> CreateApplicationAsync(
        ICompetitionQuestionStore store)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Authentication:SigningKey"] = SigningKey
        });
        builder.Services.AddNoCtfApi(
            builder.Configuration,
            includeInfrastructure: false);
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddFastEndpoints(options =>
        {
            options.DisableAutoDiscovery = true;
            options.Assemblies = [typeof(ListCompetitionQuestionsEndpoint).Assembly];
            options.Filter = type =>
                type == typeof(ListCompetitionQuestionsEndpoint)
                || type == typeof(ListCompetitionQuestionsValidator);
        });
        builder.Services.AddHttpContextAccessor();
        builder.Services.AddScoped<IUserContext, HttpUserContext>();
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
        builder.Services.AddSingleton(store);
        builder.Services.AddSingleton<ICompetitionQuestionReader>(store);
        builder.Services.AddSingleton<ICompetitionQuestionWriter>(store);
        builder.Services.AddScoped<ListCompetitionQuestions>();
        var emailVerification = Substitute.For<IEmailVerificationConfigurationStore>();
        emailVerification.GetAsync(Arg.Any<CancellationToken>()).Returns(new
            EmailVerificationConfigurationView(
                Enabled: false,
                PublicBaseUrl: "https://example.test",
                TokenLifetimeMinutes: 30,
                ResendCooldownSeconds: 60,
                PasswordResetTokenLifetimeMinutes: 30,
                PasswordResetCooldownSeconds: 60,
                PasswordResetMaxRequestsPerHour: 5,
                SmtpHost: string.Empty,
                SmtpPort: 25,
                SmtpSecurityMode: SmtpSecurityMode.None,
                SmtpUserName: string.Empty,
                SmtpPasswordConfigured: false,
                SmtpFromAddress: string.Empty,
                SmtpFromName: string.Empty,
                SmtpTimeoutSeconds: 30,
                UpdatedAt: DateTimeOffset.UnixEpoch));
        builder.Services.AddSingleton(emailVerification);

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
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
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
                    new Claim(ClaimTypes.NameIdentifier, subjectId.ToString()),
                    new Claim("user_kind", "Human")
                ],
                Scheme.Name);
            return Task.FromResult(AuthenticateResult.Success(
                new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name)));
        }
    }
}
