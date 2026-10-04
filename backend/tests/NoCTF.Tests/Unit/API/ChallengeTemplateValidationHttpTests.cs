using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json.Nodes;
using System.Security.Claims;
using System.Text.Encodings.Web;
using FastEndpoints;
using FastEndpoints.OpenApi;
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
    [Arguments(GameModeProtocol.Ctf)]
    [Arguments(GameModeProtocol.Awd)]
    [Arguments(GameModeProtocol.Awdp)]
    [Arguments(GameModeProtocol.Koh)]
    public async Task Create_template_accepts_browser_ordered_definition(GameModeProtocol mode)
    {
        await using var app = await CreateApplicationAsync();
        using var client = app.GetTestClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue(BearerScheme, "test-token");
        var definition = new JsonObject();
        definition[mode.ToString().ToLowerInvariant()] = mode switch
        {
            GameModeProtocol.Ctf => new JsonObject { ["interactionKind"] = "FlagSubmission" },
            GameModeProtocol.Awd => new JsonObject { ["flagInjection"] = null },
            _ => new JsonObject()
        };
        definition["patchCommand"] = new JsonArray();
        definition["mode"] = mode.ToString();
        var payload = new JsonObject
        {
            ["mode"] = mode.ToString(),
            ["visibility"] = "Private",
            ["title"] = $"{mode} template",
            ["direction"] = "Web",
            ["definition"] = definition
        };
        using var body = new StringContent(payload.ToJsonString(), Encoding.UTF8, "application/json");

        using var response = await client.PostAsync("/api/v1/admin/challenges", body);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Created);
    }

    [Test]
    public async Task Create_template_without_definition_mode_returns_400()
    {
        await using var app = await CreateApplicationAsync();
        using var client = app.GetTestClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue(BearerScheme, "test-token");

        using var response = await client.PostAsJsonAsync("/api/v1/admin/challenges", new
        {
            mode = "Ctf",
            visibility = "Private",
            title = "Template",
            direction = "Web",
            definition = new { interactionKind = "FlagSubmission" }
        });

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
    }

    [Test]
    public async Task Missing_definition_mode_is_a_client_error()
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
                    mode = "Ctf",
                    visibility = "Private",
                    title = "Template",
                    direction = "Web",
                    definition = new { runtime = (object?)null }
                }
            });

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
    }

    [Test]
    public async Task Missing_nested_runtime_kind_is_a_client_error()
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
                    mode = "Ctf",
                    visibility = "Private",
                    title = "Template",
                    direction = "Web",
                    definition = new
                    {
                        mode = "Ctf",
                        interactionKind = "FlagSubmission",
                        runtime = new { allocation = "PerTeam" }
                    }
                }
            });

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
    }

    [Test]
    public async Task Unknown_definition_mode_remains_a_client_error()
    {
        await using var app = await CreateApplicationAsync();
        using var client = app.GetTestClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue(BearerScheme, "test-token");

        using var response = await client.PostAsJsonAsync("/api/v1/admin/challenges", new
        {
            mode = "Ctf",
            visibility = "Private",
            title = "Template",
            direction = "Web",
            definition = new { mode = "Unknown", interactionKind = "FlagSubmission" }
        });

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
    }

    [Test]
    [Arguments("""{"mode":"Ctf"}""")]
    [Arguments("""{"mode":"Ctf","awd":{"flagInjection":null}}""")]
    [Arguments("""{"mode":"Ctf","ctf":{"interactionKind":"FlagSubmission"},"awd":{"flagInjection":null}}""")]
    [Arguments("""{"mode":"Ctf","ctf":{"interactionKind":"FlagSubmission"},"runtime":{"kind":"Container","ova":{"sourceUrl":"https://example.test/a.ova","sha256":"abc"}}}""")]
    [Arguments("""{"mode":"Ctf","ctf":{"interactionKind":"FlagSubmission"},"runtime":{"kind":"Container","container":{"image":"alpine","command":[],"environment":{},"labels":{},"portMappings":[],"security":null,"flagEnvironmentVariableName":null,"internalPorts":[]}}}""")]
    public async Task Invalid_definition_or_runtime_branch_returns_400(string definitionJson)
    {
        await using var app = await CreateApplicationAsync();
        using var client = app.GetTestClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue(BearerScheme, "test-token");
        var payload = new JsonObject
        {
            ["mode"] = "Ctf",
            ["visibility"] = "Private",
            ["title"] = "Template",
            ["direction"] = "Web",
            ["definition"] = JsonNode.Parse(definitionJson)
        };

        using var response = await client.PostAsync("/api/v1/admin/challenges",
            new StringContent(payload.ToJsonString(), Encoding.UTF8, "application/json"));

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
    }

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
                definition = Definition(GameModeProtocol.Ctf)
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
                definition = (object?)null
            }
        });
        await AssertValidationProblemAsync(
            applicationResponse,
            "Content.Definition");
    }

    [Test]
    [Arguments(GameModeProtocol.Ctf)]
    [Arguments(GameModeProtocol.Awd)]
    [Arguments(GameModeProtocol.Awdp)]
    [Arguments(GameModeProtocol.Koh)]
    public async Task Minimal_definition_updates_succeed_for_every_game_mode(
        GameModeProtocol mode)
    {
        await using var app = await CreateApplicationAsync(
            mode: (NoCTF.Domain.Competitions.GameMode)mode);
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
                    definition = Definition(mode)
                }
            });

        if (response.StatusCode != HttpStatusCode.OK)
            throw new InvalidOperationException(await response.Content.ReadAsStringAsync());
    }

    private static ChallengeDefinitionContract Definition(GameModeProtocol mode)
    {
        ChallengeDefinitionContract definition = mode switch
        {
            GameModeProtocol.Ctf => new ChallengeDefinitionContract
            {
                Mode = mode,
                Ctf = new CtfChallengeDefinitionContract
                {
                    InteractionKind = NoCTF.API.Endpoints.Challenges.CtfInteractionKindProtocol.FlagSubmission
                }
            },
            GameModeProtocol.Awd => new ChallengeDefinitionContract
            {
                Mode = mode,
                Awd = new AwdChallengeDefinitionContract { FlagInjection = null }
            },
            GameModeProtocol.Awdp => new ChallengeDefinitionContract
            {
                Mode = mode,
                Awdp = new AwdpChallengeDefinitionContract(),
                MaximumPatchUploadBytes = 268_435_456
            },
            GameModeProtocol.Koh => new ChallengeDefinitionContract
            {
                Mode = mode,
                Koh = new KohChallengeDefinitionContract()
            },
            _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, null)
        };
        definition.PatchCommand = [];
        return definition;
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
        Guid[]? managerIds = null,
        NoCTF.Domain.Competitions.GameMode mode = NoCTF.Domain.Competitions.GameMode.Ctf)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddProblemDetails();
        builder.Services.AddExceptionHandler<RequestSafetyExceptionHandler>();
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddFastEndpoints(options =>
        {
            options.DisableAutoDiscovery = true;
            options.Assemblies = [typeof(PatchChallengeTemplateEndpoint).Assembly];
            options.Filter = type =>
                type == typeof(PatchChallengeTemplateEndpoint)
                || type == typeof(PatchChallengeTemplateValidator)
                || type == typeof(CreateChallengeTemplateEndpoint)
                || type == typeof(CreateChallengeTemplateValidator);
        });
        builder.Services.OpenApiDocument();
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
        store.CreateAsync(
                Arg.Any<CreateChallengeTemplateCommand>(),
                Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var command = call.Arg<CreateChallengeTemplateCommand>()!;
                return Task.FromResult(new ChallengeTemplateWriteResult(
                    ChallengeTemplateWriteState.Succeeded,
                    new ChallengeTemplateView(
                        command.ChallengeId ?? Guid.CreateVersion7(),
                        command.OwnerId,
                        [],
                        command.Mode,
                        command.Visibility,
                        command.Title,
                        command.Description,
                        command.Direction,
                        command.Definition,
                        null,
                        0,
                        command.CreatedAt,
                        command.CreatedAt)));
            });
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
                    mode,
                    NoCTF.Domain.Challenges.ChallengeVisibility.Private,
                    "Template",
                    null,
                    "Web",
                    ChallengeDefinitionContractMapper.ToDomain(
                        challengeId,
                        mode,
                        Definition((GameModeProtocol)mode)),
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
                        command.Definition,
                        null,
                        0,
                        command.UpdatedAt,
                        command.UpdatedAt)));
            });
        builder.Services.AddSingleton<IChallengeBankStore>(store);
        builder.Services.AddSingleton<IChallengeConfigurationCatalog>(
            new GameModeChallengeConfigurationCatalog());
        builder.Services.AddScoped<GetChallengeTemplate>();
        builder.Services.AddScoped<CreateChallengeTemplate>();
        builder.Services.AddScoped<UpdateChallengeTemplate>();
        builder.Services.AddScoped<UpdateChallengeTemplatePermissions>();
        builder.Services.AddScoped<TransferChallengeTemplateOwner>();
        builder.Services.AddSingleton<IAtomicAggregatePatch, TestAtomicAggregatePatch>();

        var app = builder.Build();
        app.UseExceptionHandler();
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
                [
                    new Claim(ClaimTypes.NameIdentifier, ActorId.ToString()),
                    new Claim(ClaimTypes.Role, "Organizer")
                ],
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
