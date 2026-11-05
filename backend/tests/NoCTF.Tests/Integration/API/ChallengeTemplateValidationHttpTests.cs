using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using FastEndpoints;
using FastEndpoints.Swagger;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NoCTF.API.Composition;
using NoCTF.API.Endpoints.Administration.ChallengeBank;
using NoCTF.API.Security;
using NoCTF.Application.Authentication.Account;
using NoCTF.Application.Challenges.Bank;
using NoCTF.Application.Challenges.Configuration;
using NoCTF.Application.Messaging;
using NoCTF.Domain.Challenges.Capabilities;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Identity;
using NoCTF.Domain.Identity.Mfa;
using NoCTF.Infrastructure.Authentication;
using NoCTF.Infrastructure.Challenges.Bank;
using NoCTF.Infrastructure.Persistence;
using System.Formats.Tar;
using System.IO.Compression;
using FluentStorage;
using FluentStorage.Storage;
using Microsoft.Extensions.Logging.Abstractions;
using NoCTF.Application.Challenges.Testing;
using NoCTF.Application.Challenges.Capabilities;
using NoCTF.Application.Runtime.Provisioning;
using NoCTF.Application.Storage;
using NoCTF.Domain.Challenges;
using NoCTF.Domain.Runtime;
using NoCTF.GameModes.Registration;
using NoCTF.Infrastructure.Challenges.Testing;
using NoCTF.Infrastructure.Challenges.Capabilities;
using NoCTF.Infrastructure.Runtime.Placement;
using NoCTF.Infrastructure.Storage;

namespace NoCTF.Tests.Integration.API;

[Category("Integration"), NotInParallel]
public sealed class ChallengeTemplateValidationHttpTests
{
    [Test, Timeout(300_000)]
    public async Task Browser_ordered_capability_branches_create_real_bank_material_without_competition_fields(CancellationToken ct)
    {
        await using var fixture = await ChallengeApplicationFixture.CreateAsync(CompetitionStatus.Draft, ct);
        await using var server = await StartAsync(fixture, ct);
        using var client = Client(server, fixture);
        foreach (var capability in Enum.GetValues<ChallengeCapabilityKind>())
        {
            var definition = Definition(capability);
            var payload = new JsonObject { ["definition"] = definition, ["direction"] = "Web", ["title"] = $"Capability {capability}", ["visibility"] = "Private" };
            using var response = await client.PostAsync("/api/v1/admin/challenges", new StringContent(payload.ToJsonString(), Encoding.UTF8, "application/json"), ct);
            if (response.StatusCode != HttpStatusCode.Created)
                throw new InvalidOperationException($"Capability {capability} was rejected with {response.StatusCode}: {await response.Content.ReadAsStringAsync(ct)}");
            var content = JsonNode.Parse(await response.Content.ReadAsStringAsync(ct))!.AsObject();
            await Assert.That(content.ContainsKey("mode")).IsFalse();
            await Assert.That(content["definition"]!.AsObject().ContainsKey("mode")).IsFalse();
            var id = content["id"]!.GetValue<Guid>(); fixture.Db.ChangeTracker.Clear();
            var saved = await fixture.Db.Challenges.AsSplitQuery().SingleAsync(value => value.Id == id, ct);
            await Assert.That(saved.OwnerId).IsEqualTo(fixture.OwnerId);
            await Assert.That(saved.Definition!.Capabilities.Single().Kind).IsEqualTo(capability);
        }
        await Assert.That(await fixture.Db.CompetitionChallenges.CountAsync(ct)).IsEqualTo(0);
    }

    [Test, Timeout(300_000)]
    public async Task Invalid_capability_shapes_are_rejected_before_any_bank_write(CancellationToken ct)
    {
        await using var fixture = await ChallengeApplicationFixture.CreateAsync(CompetitionStatus.Draft, ct);
        await using var server = await StartAsync(fixture, ct); using var client = Client(server, fixture);
        var valid = Definition(ChallengeCapabilityKind.Break);
        var empty = new JsonObject { ["capabilities"] = new JsonArray() };
        var duplicate = valid.DeepClone().AsObject(); duplicate["capabilities"]!.AsArray().Add(duplicate["capabilities"]![0]!.DeepClone());
        var unknown = valid.DeepClone().AsObject(); unknown["capabilities"]![0]!["kind"] = "Unknown";
        var mixed = valid.DeepClone().AsObject(); mixed["capabilities"]![0]!["fix"] = new JsonObject { ["targetServiceName"] = "web" };
        var absentBranch = valid.DeepClone().AsObject(); absentBranch["capabilities"]![0]!.AsObject().Remove("break");
        var runtimeKindMissing = Definition(ChallengeCapabilityKind.Fix); runtimeKindMissing["runtime"]!.AsObject().Remove("kind");
        foreach (var definition in new[] { empty, duplicate, unknown, mixed, absentBranch, runtimeKindMissing })
        {
            var payload = new JsonObject { ["title"] = "Invalid", ["visibility"] = "Private", ["direction"] = "Web", ["definition"] = definition };
            using var response = await client.PostAsJsonAsync("/api/v1/admin/challenges", payload, ct);
            await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
        }
        await Assert.That(await fixture.Db.Challenges.CountAsync(ct)).IsEqualTo(1);
    }

    [Test, Timeout(300_000)]
    public async Task Saved_capability_tests_enforce_HTTP_authorization_scope_and_immutable_upload_receipts(CancellationToken ct)
    {
        await using var runtime = await RuntimeExecutionFixture.CreateAsync(ct);
        var fixture = runtime.Application;
        var db = fixture.Db;
        var revisionId = Guid.NewGuid(); var questionId = Guid.NewGuid(); var sourceId = Guid.NewGuid(); var optionId = Guid.NewGuid();
        var definition = new ChallengeDefinition
        {
            Runtime = new ContainerChallengeRuntimeTemplate { Services = [new() { Name = "service-0", Image = RuntimeExecutionFixture.Image,
                CpuCores = .1m, MemoryMiB = 64,
                Commands = new[] { "nc", "-ll", "-p", "8080", "-e", "/bin/cat" }.Select((value, position) => new ChallengeRuntimeServiceCommand { Position = position, Value = value }).ToList(),
                InternalPorts = [new() { Port = 8080 }, new() { Port = 8081 }] }],
                UrlBindings = [new() { Position = 0, ServiceName = "service-0", ContainerPort = 8080,
                    Exposure = PersistedRuntimeExposure.OwnerOnly, UrlTemplate = "http://{HOST}:{PORT}" }] },
            Capabilities = [new BreakCapabilityDefinition(), new FixCapabilityDefinition { TargetServiceName = "service-0", Entrypoint = "fix.sh" },
                new CheckCapabilityDefinition { TargetServiceName = "service-0", Image = CapabilityCallbackFixture.PythonImage,
                    Requirements = [new() { Kind = CheckItemKind.Functionality }] },
                new ControlCapabilityDefinition { ServiceName = "service-0", ContainerPort = 8081, TimeoutSeconds = 1, UrlTemplate = "http://{HOST}:{PORT}/control" },
                new QuestionnaireCapabilityDefinition { CurrentRevisionId = revisionId, CurrentRevision = new() { Id = revisionId, Number = 1,
                    Questions = [new() { Id = questionId, SourceId = sourceId, Title = "Saved choice", Body = "Choose one.", Options =
                        [new() { Id = optionId, Content = "Correct", IsCorrect = true }, new() { Id = Guid.NewGuid(), Position = 1, Content = "Wrong" }] }] } }]
        };
        var outsiderId = Guid.NewGuid();
        db.Users.Add(new() { Id = outsiderId, UserName = "outsider", NormalizedUserName = "OUTSIDER", Email = "outsider@example.test",
            NormalizedEmail = "OUTSIDER@EXAMPLE.TEST", Role = UserRole.Organizer, AccountStatus = UserAccountStatus.Active });
        await db.SaveChangesAsync(ct);
        await using var server = await StartAsync(fixture, ct);
        using var client = Client(server, fixture);
        using var created = await client.PostAsJsonAsync("/api/v1/admin/challenges", new
        {
            title = "Saved capability tests", visibility = "Private", direction = "Web",
            definition = ChallengeDefinitionContractMapper.FromDomain(definition)
        }, ct);
        await Assert.That(created.StatusCode).IsEqualTo(HttpStatusCode.Created);
        var templateId = (await created.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("id").GetGuid();
        const string flag = "flag{authorized-template-test}";
        db.ChallengeFlags.Add(new TemplateChallengeFlag { Id = Guid.NewGuid(), ChallengeId = templateId, Flag = flag,
            FlagSha256 = NoCTF.Application.Challenges.Flags.ManageChallengeFlags.Hash(flag), MatchKind = ChallengeFlagMatchKind.Exact, CreatedAt = DateTimeOffset.UtcNow });
        await db.SaveChangesAsync(ct);
        var queued = await runtime.QueueAsync(1, ct, new TemplateTestRuntimeInstance { Id = Guid.NewGuid(), ChallengeId = templateId,
            RuntimeKind = RuntimeKind.Container, RuntimeProvider = RuntimeProvider.Docker, State = RuntimeState.Queued,
            TestFlagDelivery = RuntimeTestFlagDelivery.NotRequired, TestFlagState = RuntimeTestFlagState.NotRequired, CreatedAt = DateTimeOffset.UtcNow });
        var provisionReply = await runtime.Handler.ProvisionContainerAsync(queued, ct);
        var provisioned = provisionReply as NoCTF.Application.Runtime.Instances.RuntimeProvisioned
            ?? throw new InvalidOperationException($"The real HTTP capability-test target did not start: {provisionReply}.");
        await runtime.PersistAsync(provisioned, ct);
        var root = $"/api/v1/admin/challenges/{templateId}";
        using var anonymous = new HttpClient { BaseAddress = client.BaseAddress };
        using var anonymousResult = await anonymous.PostAsJsonAsync(root + "/break-tests", new { flag }, ct);
        await Assert.That(anonymousResult.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
        using var outsider = Client(server, fixture, outsiderId);
        using var forbidden = await outsider.PostAsJsonAsync(root + "/break-tests", new { flag }, ct);
        await Assert.That(forbidden.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
        using var broken = await client.PostAsJsonAsync(root + "/break-tests", new { flag = "" }, ct);
        await Assert.That(broken.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
        await Assert.That(broken.Content.Headers.ContentType!.MediaType).IsEqualTo("application/problem+json");
        using var matching = await client.PostAsJsonAsync(root + "/break-tests", new { flag }, ct);
        await Assert.That(matching.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That((await matching.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("match").GetString()).IsEqualTo("Matched");
        await Assert.That(matching.Headers.CacheControl!.NoStore).IsTrue();
        using var answers = await client.PostAsJsonAsync(root + "/questionnaire-tests", new { answers = new[] { new { questionId, selectedOptionIds = new[] { optionId } } } }, ct);
        await Assert.That(answers.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That((await answers.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("questions")[0].GetProperty("result").GetString()).IsEqualTo("Correct");
        using var invalidAnswers = await client.PostAsJsonAsync(root + "/questionnaire-tests", new { answers = new[] { new { questionId, selectedOptionIds = new[] { Guid.NewGuid() } } } }, ct);
        await Assert.That(invalidAnswers.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
        await Assert.That(invalidAnswers.Content.Headers.ContentType!.MediaType).IsEqualTo("application/problem+json");
        using var control = await client.PostAsJsonAsync(root + "/control-tests", new { runtimeInstanceId = provisioned.RuntimeInstanceId }, ct);
        await Assert.That(control.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That((await control.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("result").GetString()).IsEqualTo("Indeterminate");
        using var check = await client.PostAsJsonAsync(root + "/check-tests", new { runtimeInstanceId = provisioned.RuntimeInstanceId, requestId = Guid.NewGuid() }, ct);
        await Assert.That(check.StatusCode).IsEqualTo(HttpStatusCode.Accepted);
        using var wrongTarget = await client.PostAsJsonAsync(root + "/check-tests", new { runtimeInstanceId = Guid.NewGuid(), requestId = Guid.NewGuid() }, ct);
        await Assert.That(wrongTarget.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
        var requestId = Guid.NewGuid();
        var archives = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        async Task<JsonElement> Upload(Guid key, string script, HttpStatusCode expected)
        {
            if (!archives.TryGetValue(script, out var archive))
            {
                using var bytes = new MemoryStream();
                using (var gzip = new GZipStream(bytes, CompressionLevel.Fastest, true))
                using (var tar = new TarWriter(gzip, TarEntryFormat.Pax, true))
                using (var input = new MemoryStream(Encoding.UTF8.GetBytes(script)))
                    tar.WriteEntry(new PaxTarEntry(TarEntryType.RegularFile, "fix.sh") { DataStream = input });
                archive = bytes.ToArray(); archives.Add(script, archive);
            }
            using var form = new MultipartFormDataContent();
            form.Add(new StringContent(key.ToString("D")), "requestId");
            form.Add(new ByteArrayContent(archive), "file", "fix.tar.gz");
            using var result = await client.PostAsync(root + "/fix-tests", form, ct);
            await Assert.That(result.StatusCode).IsEqualTo(expected);
            return await result.Content.ReadFromJsonAsync<JsonElement>(ct);
        }
        var first = await Upload(requestId, "exit 0\n", HttpStatusCode.Accepted);
        var replay = await Upload(requestId, "exit 0\n", HttpStatusCode.Accepted);
        await Assert.That(replay.GetProperty("executionId").GetGuid()).IsEqualTo(first.GetProperty("executionId").GetGuid());
        await Upload(requestId, "exit 1\n", HttpStatusCode.Conflict);
        using var current = await client.GetAsync(first.GetProperty("statusUrl").GetString(), ct);
        await Assert.That(current.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That((await current.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("state").GetString()).IsEqualTo("Queued");
        using var privateStatus = await outsider.GetAsync(first.GetProperty("statusUrl").GetString(), ct);
        await Assert.That(privateStatus.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
        db.ChangeTracker.Clear();
        await Assert.That((await db.RuntimeInstances.SingleAsync(value => value.Id == provisioned.RuntimeInstanceId, ct)).State).IsEqualTo(RuntimeState.Running);
        await Assert.That(await db.GameplayFacts.CountAsync(ct)).IsEqualTo(0);
        await Assert.That(await db.QuestionnaireSessions.CountAsync(ct)).IsEqualTo(0);
    }

    private static JsonObject Definition(ChallengeCapabilityKind kind)
    {
        var definition = new NoCTF.Domain.Challenges.ChallengeDefinition();
        var revisionId = Guid.NewGuid();
        if (kind is ChallengeCapabilityKind.Fix or ChallengeCapabilityKind.Check or ChallengeCapabilityKind.Control)
            definition.Runtime = new NoCTF.Domain.Challenges.ContainerChallengeRuntimeTemplate { Services = [new() { Name = "web", Image = "challenge:1", InternalPorts = [new() { Port = 8080 }] }] };
        definition.Capabilities.Add(kind switch
        {
            ChallengeCapabilityKind.Break => new BreakCapabilityDefinition(),
            ChallengeCapabilityKind.Fix => new FixCapabilityDefinition { TargetServiceName = "web" },
            ChallengeCapabilityKind.Check => new CheckCapabilityDefinition { TargetServiceName = "web", Image = "checker:1", Requirements = [new() { Kind = CheckItemKind.Functionality }] },
            ChallengeCapabilityKind.Control => new ControlCapabilityDefinition { ServiceName = "web", ContainerPort = 8080, UrlTemplate = "http://{HOST}:{PORT}/owner" },
            ChallengeCapabilityKind.Questionnaire => new QuestionnaireCapabilityDefinition { CurrentRevisionId = revisionId, CurrentRevision = new() { Id = revisionId, Number = 1, Questions = [new() { Id = Guid.NewGuid(),
                SourceId = Guid.NewGuid(), Title = "Choice", Body = "Choose one.", Options = [new() { Id = Guid.NewGuid(), Content = "First", IsCorrect = true }, new() { Id = Guid.NewGuid(), Position = 1, Content = "Second" }] }] } },
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        });
        return JsonSerializer.SerializeToNode(ChallengeDefinitionContractMapper.FromDomain(definition), new JsonSerializerOptions(JsonSerializerDefaults.Web))!.AsObject();
    }

    private static HttpClient Client(WebApplication server, ChallengeApplicationFixture fixture, Guid? actorId = null)
    {
        var client = new HttpClient { BaseAddress = new Uri(server.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single()) };
        var key = server.Configuration["Authentication:SigningKey"]!;
        var token = new JwtIssuer(Options.Create(new AuthenticationTokenOptions { SigningKey = key }), TimeProvider.System).Issue(
            new AuthenticatedUser(actorId ?? fixture.OwnerId, "author", UserRole.Organizer, UserKind.Human, 0),
            new AuthenticationContext(AuthenticationMethod.Password, fixture.Now), fixture.Now);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.Token); return client;
    }

    private static async Task<WebApplication> StartAsync(ChallengeApplicationFixture fixture, CancellationToken ct)
    {
        var key = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Authentication:SigningKey"] = key, ["RunnerScoring:SigningKey"] = key }).Build();
        var builder = WebApplication.CreateBuilder(); builder.Configuration.AddConfiguration(configuration); builder.WebHost.UseKestrel().UseUrls("http://127.0.0.1:0");
        builder.Services.AddFastEndpoints(options => { options.DisableAutoDiscovery = true; options.Assemblies = [typeof(CreateChallengeTemplateEndpoint).Assembly];
            options.Filter = type => type == typeof(CreateChallengeTemplateEndpoint) || type == typeof(CreateChallengeTemplateValidator)
                || type.Namespace == typeof(TestChallengeBreakEndpoint).Namespace && type.Name.StartsWith("TestChallenge", StringComparison.Ordinal)
                || type == typeof(GetChallengeCapabilityTestEndpoint) || type == typeof(GetChallengeCapabilityTestValidator); });
        builder.Services.SwaggerDocument(); builder.Services.AddNoCtfAuthentication(configuration);
        builder.Services.AddHttpContextAccessor(); builder.Services.AddScoped<IUserContext, HttpUserContext>();
        builder.Services.AddDbContext<NoCtfDbContext>(options => options.UseNpgsql(fixture.Db.Database.GetConnectionString()));
        builder.Services.AddSingleton(fixture.MessageBus); builder.Services.AddScoped<IPostCommitMessagePublisher, NoCTF.Infrastructure.Messaging.WolverinePostCommitMessagePublisher>();
        builder.Services.AddScoped<IChallengeBankStore, ChallengeBankStore>(); builder.Services.AddSingleton<IChallengeConfigurationCatalog>(fixture.Catalog); builder.Services.AddScoped<CreateChallengeTemplate>();
        builder.Services.AddSingleton<IChallengeRuntimeTemplateCatalog, ChallengeRuntimeTemplateCatalog>();
        builder.Services.AddSingleton<IRuntimePlacementPolicy>(new ConfiguredRuntimePlacementPolicy(Options.Create(new RuntimePlacementOptions())));
        builder.Services.AddScoped<ITemplateCapabilityTestStore, TemplateCapabilityTestStore>();
        builder.Services.AddSingleton<IStore>(_ => StorageFactory.Disk(Path.GetFullPath(Path.Combine("artifacts", "capability-http-files", Guid.NewGuid().ToString("N")))));
        builder.Services.AddScoped<ManagedFileUploads>(services => new(new ManagedFileUploadRegistry(services.GetRequiredService<NoCtfDbContext>(),
            services.GetRequiredService<IPostCommitMessagePublisher>(), NullLogger<ManagedFileUploadRegistry>.Instance), services.GetRequiredService<IStore>()));
        builder.Services.AddHttpClient(HttpControlObservationClient.ClientName).ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { AllowAutoRedirect = false });
        builder.Services.AddScoped<IControlObservationClient, HttpControlObservationClient>();
        builder.Services.AddScoped<ObserveControl>();
        builder.Services.AddScoped<TestChallengeCapabilities>(); builder.Services.AddScoped<StartTemplateCapabilityTest>();
        builder.Services.AddSingleton<IOptions<EmailVerificationProtectionOptions>>(Options.Create(new EmailVerificationProtectionOptions { EncryptionKey = key }));
        builder.Services.AddSingleton<PlatformSecretProtector>();
        builder.Services.AddSingleton<NoCTF.Application.Authentication.Mfa.IMfaCryptography, NoCTF.Infrastructure.Authentication.Mfa.MfaCryptography>();
        builder.Services.AddScoped<NoCTF.Application.Authentication.Mfa.IMfaAuthenticationStore, NoCTF.Infrastructure.Authentication.Mfa.MfaAuthenticationStore>();
        var app = builder.Build(); app.UseAuthentication(); app.UseAuthorization(); app.UseNoCtfEndpoints(); await app.StartAsync(ct); return app;
    }
}
