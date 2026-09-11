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
using NoCTF.Application.Common;
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

        using var automaticResponse = await client.PatchAsJsonAsync(route, new
        {
            content = new
            {
                mode = "Ctf",
                visibility = "Private",
                title = "",
                description = (string?)null,
                direction = "",
                definitionJson = "{}"
            }
        });
        await AssertValidationProblemAsync(
            automaticResponse,
            "Content.Title",
            "Content.Direction");

        using var applicationResponse = await client.PatchAsJsonAsync(route, new
        {
            content = new
            {
                mode = "Ctf",
                visibility = "Private",
                title = "Template",
                description = (string?)null,
                direction = "Web",
                definitionJson = "not-json"
            }
        });
        await AssertValidationProblemAsync(
            applicationResponse,
            "Content.DefinitionJson");
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

        using var response = await client.PatchAsJsonAsync(
            $"/api/v1/admin/challenges/{Guid.NewGuid()}",
            new
            {
                content = new
                {
                    mode = mode.ToString(),
                    visibility = "Private",
                    title = $"{mode} template",
                    description = (string?)null,
                    direction = "Pwn",
                    definitionJson
                }
            });

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
    }

    [Test]
    public async Task Template_manager_cannot_invoke_owner_permissions_mapper()
    {
        await using var app = await CreateApplicationAsync(
            isAdministrator: false,
            ownerId: Guid.NewGuid(),
            managerIds: [ActorId]);
        using var client = app.GetTestClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue(BearerScheme, "test-token");

        using var response = await client.PatchAsJsonAsync(
            $"/api/v1/admin/challenges/{Guid.NewGuid()}",
            new
            {
                permissions = new
                {
                    ownerId = Guid.NewGuid(),
                    managerIds = Array.Empty<Guid>()
                }
            });

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Forbidden);
    }

    private static async Task<WebApplication> CreateApplicationAsync(
        bool isAdministrator = true,
        Guid? ownerId = null,
        Guid[]? managerIds = null)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddProblemDetails();
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddFastEndpoints(options =>
        {
            options.DisableAutoDiscovery = true;
            options.Assemblies = [typeof(PatchChallengeTemplateEndpoint).Assembly];
            options.Filter = type =>
                type == typeof(PatchChallengeTemplateEndpoint)
                || type == typeof(PatchChallengeTemplateValidator);
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
        user.IsAdministrator.Returns(isAdministrator);
        builder.Services.AddSingleton<IUserContext>(user);
        var store = Substitute.For<IChallengeBankStore>();
        store.FindAsync(
                Arg.Any<Guid>(),
                Arg.Any<Guid>(),
                Arg.Any<bool>(),
                Arg.Any<bool>(),
                Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var challengeId = call.ArgAt<Guid>(0);
                return Task.FromResult<ChallengeTemplateView?>(new(
                    challengeId,
                    ownerId ?? ActorId,
                    managerIds ?? [],
                    NoCTF.Domain.Competitions.GameMode.Ctf,
                    NoCTF.Domain.Challenges.ChallengeVisibility.Private,
                    "Template",
                    null,
                    "Web",
                    "{}",
                    null,
                    0,
                    DateTimeOffset.UtcNow,
                    DateTimeOffset.UtcNow));
            });
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
        builder.Services.AddScoped<GetChallengeTemplate>();
        builder.Services.AddScoped<UpdateChallengeTemplate>();
        builder.Services.AddScoped<UpdateChallengeTemplatePermissions>();
        builder.Services.AddScoped<TransferChallengeTemplateOwner>();
        builder.Services.AddSingleton<IAtomicAggregatePatch, TestAtomicAggregatePatch>();

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

    private sealed class TestAtomicAggregatePatch : IAtomicAggregatePatch
    {
        public async Task<TResult> ExecuteAsync<TResult>(
            Func<CancellationToken, Task<AtomicAggregatePatchDecision<TResult>>> operation,
            CancellationToken cancellationToken = default) =>
            (await operation(cancellationToken)).Result;
    }
}
