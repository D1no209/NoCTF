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
using NoCTF.API.Endpoints.Administration.GameplayFacts;
using NoCTF.API.Security;
using NoCTF.Application.GameplayFacts.AdjudicationPreview;
using NoCTF.Application.Teams.Moderation;
using NoCTF.Application.Authentication.Account;
using NoCTF.Application.Authentication.EmailVerification;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Identity;

namespace NoCTF.Tests.Unit.API;

public sealed class HistoricalAdjudicationPreviewHttpTests
{
    private const string BearerScheme = "Bearer";

    [Test]
    public async Task Observer_can_read_preview_but_participant_is_forbidden()
    {
        var competitionId = Guid.NewGuid();
        var authorizer = Substitute.For<ICompetitionModerationAuthorizer>();
        authorizer.CanObserveAsync(Arg.Any<Guid>(), competitionId, Arg.Any<CancellationToken>())
            .Returns(true, false);
        await using var app = await CreateApplicationAsync(authorizer);
        using var client = app.GetTestClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue(BearerScheme, Guid.NewGuid().ToString());
        var route = $"/api/v1/admin/competitions/{competitionId}/gameplay-facts/adjudication-differences";

        using var observer = await client.GetAsync(route);
        using var participant = await client.GetAsync(route);

        await Assert.That(observer.StatusCode).IsEqualTo(HttpStatusCode.OK);
        var payload = await observer.Content
            .ReadFromJsonAsync<HistoricalAdjudicationDifferencePageResponse>();
        await Assert.That(payload).IsNotNull();
        await Assert.That(payload!.Items).IsEmpty();
        await Assert.That(participant.StatusCode).IsEqualTo(HttpStatusCode.Forbidden);
    }

    private static async Task<WebApplication> CreateApplicationAsync(
        ICompetitionModerationAuthorizer authorizer)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddNoCtfApi(builder.Configuration, includeInfrastructure: false);
        builder.Services.AddFastEndpoints(options =>
        {
            options.DisableAutoDiscovery = true;
            options.Assemblies = [typeof(PreviewHistoricalAdjudicationDifferencesEndpoint).Assembly];
            options.Filter = type =>
                type == typeof(PreviewHistoricalAdjudicationDifferencesEndpoint)
                || type == typeof(PreviewHistoricalAdjudicationDifferencesValidator);
        });
        builder.Services
            .AddAuthentication(options =>
            {
                options.DefaultAuthenticateScheme = BearerScheme;
                options.DefaultChallengeScheme = BearerScheme;
            })
            .AddScheme<AuthenticationSchemeOptions, TestBearerHandler>(BearerScheme, _ => { });
        builder.Services.AddAuthorization();
        builder.Services.AddSingleton(authorizer);
        var store = Substitute.For<IHistoricalAdjudicationEvidenceStore>();
        store.ReadAsync(
                Arg.Any<Guid>(),
                Arg.Any<Guid?>(),
                Arg.Any<DateTimeOffset?>(),
                Arg.Any<Guid?>(),
                Arg.Any<int>(),
                Arg.Any<CancellationToken>())
            .Returns(new HistoricalAdjudicationEvidencePage(GameMode.Ctf, []));
        builder.Services.AddSingleton(store);
        builder.Services.AddSingleton<PreviewHistoricalAdjudicationDifferences>();
        var user = Substitute.For<IUserContext>();
        user.UserId.Returns(Guid.NewGuid());
        builder.Services.AddSingleton(user);
        var emailConfiguration = Substitute.For<IEmailVerificationConfigurationStore>();
        emailConfiguration.GetAsync(Arg.Any<CancellationToken>()).Returns(
            new EmailVerificationConfigurationView(
                false, "https://example.test", 30, 60, 30, 60, 3,
                string.Empty, 25, SmtpSecurityMode.StartTls, string.Empty,
                false, string.Empty, string.Empty, 10, 0, DateTimeOffset.UtcNow));
        builder.Services.AddSingleton(emailConfiguration);
        builder.Services.AddSingleton(Substitute.For<IUserAuthenticationStore>());

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
                [new Claim(ClaimTypes.NameIdentifier, subjectId.ToString())],
                Scheme.Name);
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(
                new ClaimsPrincipal(identity), Scheme.Name)));
        }
    }
}
