using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using FastEndpoints;
using FastEndpoints.Swagger;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using NoCTF.API.Composition;
using NoCTF.API.Endpoints.Administration.ChallengeBank;
using NoCTF.API.Endpoints.Competitions;
using NoCTF.API.Security;
using NoCTF.Application.Challenges.Bank;
using NoCTF.Application.Challenges.Configuration;
using NoCTF.GameModes.Registration;

namespace NoCTF.Tests.Unit.API;

public sealed class ChallengeTemplateValidationHttpTests
{
    private const string BearerScheme = "Bearer";
    private static readonly Guid ActorId = Guid.Parse(
        "a6500287-4946-476e-933d-19a31ea5f2b9");

    [Test]
    public async Task Automatic_and_application_validation_share_problem_details_contract()
    {
        await using var app = await CreateApplicationAsync();
        using var client = app.GetTestClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue(BearerScheme, "test-token");
        var route = $"/api/v1/admin/challenges/{Guid.NewGuid()}";

        using var automaticResponse = await client.PutAsJsonAsync(route, new { });
        await AssertValidationProblemAsync(
            automaticResponse,
            nameof(UpdateChallengeTemplateRequest.Mode),
            nameof(UpdateChallengeTemplateRequest.Visibility),
            nameof(UpdateChallengeTemplateRequest.Title),
            nameof(UpdateChallengeTemplateRequest.Direction));

        using var applicationResponse = await client.PutAsJsonAsync(route, new
        {
            mode = "Ctf",
            visibility = "Private",
            title = "Template",
            direction = "Web",
            definitionJson = "not-json"
        });
        await AssertValidationProblemAsync(
            applicationResponse,
            nameof(UpdateChallengeTemplateRequest.DefinitionJson));
    }

    [Test]
    [Arguments(GameModeProtocol.Ctf, "{\"schemaVersion\":2}")]
    [Arguments(GameModeProtocol.Awd, "{\"schemaVersion\":4}")]
    [Arguments(GameModeProtocol.Awdp, "{\"schemaVersion\":4,\"maximumPatchUploadBytes\":268435456}")]
    [Arguments(GameModeProtocol.Koh, "{\"schemaVersion\":1}")]
    public async Task Minimal_definition_updates_succeed_for_every_game_mode(
        GameModeProtocol mode,
        string definitionJson)
    {
        await using var app = await CreateApplicationAsync();
        using var client = app.GetTestClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue(BearerScheme, "test-token");

        using var response = await client.PutAsJsonAsync(
            $"/api/v1/admin/challenges/{Guid.NewGuid()}",
            new
            {
                mode = mode.ToString(),
                visibility = "Private",
                title = $"{mode} template",
                direction = "Pwn",
                definitionJson
            });

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
    }

    private static async Task<WebApplication> CreateApplicationAsync()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddProblemDetails();
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddFastEndpoints(options =>
        {
            options.DisableAutoDiscovery = true;
            options.Assemblies = [typeof(UpdateChallengeTemplateEndpoint).Assembly];
            options.Filter = type =>
                type == typeof(UpdateChallengeTemplateEndpoint)
                || type == typeof(UpdateChallengeTemplateValidator);
        });
        builder.Services.SwaggerDocument();
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
        user.IsAdministrator.Returns(true);
        builder.Services.AddSingleton<IUserContext>(user);
        var store = Substitute.For<IChallengeBankStore>();
        store.UpdateAsync(
                Arg.Any<UpdateChallengeTemplateCommand>(),
                Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var command = call.Arg<UpdateChallengeTemplateCommand>()!;
                return Task.FromResult(new ChallengeTemplateWriteResult(
                    ChallengeTemplateWriteState.Succeeded,
                    new ChallengeTemplateView(
                        command.ChallengeId,
                        command.ActorId,
                        [],
                        command.Mode,
                        command.Visibility,
                        command.Title,
                        command.Description,
                        command.Direction,
                        command.DefinitionJson,
                        null,
                        0,
                        command.UpdatedAt,
                        command.UpdatedAt)));
            });
        builder.Services.AddSingleton<IChallengeBankStore>(store);
        builder.Services.AddSingleton<IChallengeConfigurationCatalog>(
            new GameModeChallengeConfigurationCatalog());
        builder.Services.AddScoped<UpdateChallengeTemplate>();

        var app = builder.Build();
        app.UseAuthentication();
        app.UseAuthorization();
        app.UseNoCtfEndpoints();
        await app.StartAsync();
        return app;
    }

    private static async Task AssertValidationProblemAsync(
        HttpResponseMessage response,
        params string[] expectedErrorKeys)
    {
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
        await Assert.That(response.Content.Headers.ContentType?.MediaType)
            .IsEqualTo("application/problem+json");

        var problem =
            await response.Content.ReadFromJsonAsync<ValidationProblemDetails>();
        await Assert.That(problem).IsNotNull();
        await Assert.That(problem!.Status)
            .IsEqualTo((int)HttpStatusCode.BadRequest);
        foreach (var expectedErrorKey in expectedErrorKeys)
        {
            await Assert.That(problem.Errors.Keys.Any(key => string.Equals(
                key,
                expectedErrorKey,
                StringComparison.OrdinalIgnoreCase))).IsTrue();
        }
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
            var identity = new ClaimsIdentity(
                [new Claim(ClaimTypes.NameIdentifier, ActorId.ToString())],
                Scheme.Name);
            var ticket = new AuthenticationTicket(
                new ClaimsPrincipal(identity),
                Scheme.Name);
            return Task.FromResult(AuthenticateResult.Success(ticket));
        }
    }
}
