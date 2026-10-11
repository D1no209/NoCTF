using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using FastEndpoints;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NoCTF.API.Composition;
using NoCTF.API.Endpoints.Theory.Questions;
using NoCTF.API.Localization;
using NoCTF.API.Security;
using NoCTF.Application.Theory.Questions;
using NoCTF.Domain.Identity;
using NoCTF.Domain.Theory.Questions;
using NoCTF.Infrastructure.Persistence;
using NoCTF.Infrastructure.Theory.Questions;
using NoCTF.Tests.Fixtures.PostgreSql;
using FluentStorage;
using FluentStorage.Storage;
using NSubstitute;
using NoCTF.Application.Messaging;
using NoCTF.Application.Storage;
using NoCTF.Application.Scoring.Leaderboard;
using NoCTF.Application.Theory.Activities;
using NoCTF.Application.Theory.Papers;
using NoCTF.Domain.Theory.Activities;
using NoCTF.Infrastructure.Storage;
using NoCTF.Infrastructure.Theory.Activities;
using NoCTF.Infrastructure.Theory.Papers;

namespace NoCTF.Tests.Integration.API;

[Category("Integration")]
public sealed class TheoryQuestionHttpTests
{
    [Test, Timeout(300_000)]
    public async Task Real_question_routes_require_identity_and_keep_answers_out_of_use_only_reads(CancellationToken ct)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var postgres = new PostgreSqlContainerFixture();
            await postgres.InitializeAsync();
            var options = new DbContextOptionsBuilder<NoCtfDbContext>().UseNpgsql(postgres.ConnectionString).UseSnakeCaseNamingConvention().Options;
            var owner = Guid.NewGuid();
            var consumer = Guid.NewGuid();
            await using (var db = new NoCtfDbContext(options))
            {
                await db.Database.EnsureCreatedAsync(ct);
                db.Users.AddRange(Actor(owner, "http-theory-owner", UserRole.Organizer), Actor(consumer, "http-theory-consumer", UserRole.User));
                await db.SaveChangesAsync(ct);
            }
            var builder = WebApplication.CreateBuilder();
            builder.WebHost.UseTestServer();
            builder.Services.AddFastEndpoints(options =>
            {
                options.DisableAutoDiscovery = true;
                options.Assemblies = [typeof(CreateTheoryQuestionEndpoint).Assembly];
                options.Filter = type => type.Namespace?.StartsWith("NoCTF.API.Endpoints.Theory.", StringComparison.Ordinal) == true;
            });
            builder.Services.AddAuthentication("Bearer").AddScheme<AuthenticationSchemeOptions, TestBearer>("Bearer", _ => { });
            builder.Services.AddAuthorization();
            builder.Services.AddHttpContextAccessor();
            builder.Services.AddScoped<IUserContext, HttpUserContext>();
            builder.Services.AddSingleton(TimeProvider.System);
            builder.Services.AddDbContext<NoCtfDbContext>(options => options.UseNpgsql(postgres.ConnectionString).UseSnakeCaseNamingConvention());
            builder.Services.AddScoped<ITheoryQuestionBank, TheoryQuestionBank>();
            builder.Services.AddScoped<ManageTheoryQuestions>();
            builder.Services.AddScoped<ITheoryImageStore, TheoryImageStore>();
            builder.Services.AddScoped<ManageTheoryImages>();
            builder.Services.AddScoped<ManagedFileUploads>();
            builder.Services.AddScoped<IManagedFileUploadRegistry, ManagedFileUploadRegistry>();
            builder.Services.AddSingleton(Substitute.For<IPostCommitMessagePublisher>());
            builder.Services.AddSingleton(Substitute.For<ILeaderboardCache>());
            builder.Services.AddScoped<ITheoryActivityStore, TheoryActivityStore>();
            builder.Services.AddScoped<ITheoryPaperStore, TheoryPaperStore>();
            builder.Services.AddScoped<ITheoryReviewStore, TheoryReviewStore>();
            using var imageDirectory = new TheoryImageDirectory();
            using var imageStorage = StorageFactory.Disk(imageDirectory.Path);
            builder.Services.AddSingleton<IStore>(imageStorage);
            await using var app = builder.Build();
            app.UseAuthentication(); app.UseAuthorization(); app.UseNoCtfEndpoints();
            await app.StartAsync(ct);
            using var client = app.GetTestClient();
            const string path = "/api/v1/theory/questions";
            using (var anonymous = await client.GetAsync(path, ct))
                await Assert.That(anonymous.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
            var content = new TheoryQuestionInput("HTTP synthetic", "Private question body", "PRIVATE ANSWER EXPLANATION",
                TheoryQuestionKind.SingleChoice, TheoryQuestionDifficulty.Basic,
                [new("alpha", true), new("beta", false)], [], []);
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", consumer.ToString());
            using (var denied = await client.PostAsJsonAsync(path, new { content }, ct))
                await Assert.That(denied.StatusCode).IsEqualTo(HttpStatusCode.Forbidden);
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", owner.ToString());
            using (var malformed = await client.PostAsJsonAsync(path, new { content = content with { Options = [new("only", true)] } }, ct))
            {
                await Assert.That(malformed.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
                var error = await malformed.Content.ReadFromJsonAsync<JsonElement>(ct);
                await Assert.That(error.GetProperty("validationCode").GetString()).IsEqualTo("InvalidOptionCount");
                await Assert.That(error.GetProperty("messageKey").GetString()).IsEqualTo("api.theory.question.invalidOptionCount");
            }
            using var create = await client.PostAsJsonAsync(path, new { content }, ct);
            await Assert.That(create.StatusCode).IsEqualTo(HttpStatusCode.OK);
            var saved = (await create.Content.ReadFromJsonAsync<TheoryQuestionSaved>(ct))!;
            using var upload = new MultipartFormDataContent();
            var png = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+jWZkAAAAASUVORK5CYII=");
            upload.Add(new ByteArrayContent(png), "file", "question.png");
            using var imageResponse = await client.PostAsync($"{path}/{saved.QuestionId}/images", upload, ct);
            await Assert.That(imageResponse.StatusCode).IsEqualTo(HttpStatusCode.OK);
            var image = (await imageResponse.Content.ReadFromJsonAsync<TheoryImageResult>(ct))!;
            using (var imageRead = await client.GetAsync($"{path}/{saved.QuestionId}/images/{image.FileId}", ct))
            {
                await Assert.That(imageRead.StatusCode).IsEqualTo(HttpStatusCode.OK);
                await Assert.That(imageRead.Headers.CacheControl?.NoStore).IsTrue();
                await Assert.That(await imageRead.Content.ReadAsByteArrayAsync(ct)).IsEquivalentTo(png);
            }
            using (var grant = await client.PutAsJsonAsync($"{path}/{saved.QuestionId}/grants/{consumer}", new { permission = TheoryQuestionPermission.Use }, ct))
                await Assert.That(grant.StatusCode).IsEqualTo(HttpStatusCode.NoContent);
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", consumer.ToString());
            using (var list = await client.GetAsync(path, ct))
            {
                var json = await list.Content.ReadAsStringAsync(ct);
                await Assert.That(json.Contains("HTTP synthetic")).IsTrue();
                await Assert.That(json.Contains("Private question body")).IsFalse();
                await Assert.That(json.Contains("PRIVATE ANSWER EXPLANATION")).IsFalse();
                await Assert.That(json.Contains("isCorrect")).IsFalse();
            }
            using (var read = await client.GetAsync($"{path}/{saved.QuestionId}", ct))
                await Assert.That(read.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
            using (var imageRead = await client.GetAsync($"{path}/{saved.QuestionId}/images/{image.FileId}", ct))
                await Assert.That(imageRead.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
            using (var update = await client.PutAsJsonAsync($"{path}/{saved.QuestionId}", new { content }, ct))
                await Assert.That(update.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
            using (var grant = await client.PutAsJsonAsync($"{path}/{saved.QuestionId}/grants/{consumer}", new { permission = TheoryQuestionPermission.Manage }, ct))
                await Assert.That(grant.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", owner.ToString());
            using (var available = await client.PutAsJsonAsync($"{path}/{saved.QuestionId}/state", new { state = TheoryQuestionState.Available }, ct))
                await Assert.That(available.StatusCode).IsEqualTo(HttpStatusCode.NoContent);
            var now = DateTimeOffset.UtcNow;
            using var activityResponse = await client.PostAsJsonAsync("/api/v1/theory/activities", new { input = new TheoryActivityInput("HTTP exam",
                TheoryActivityKind.Examination, now.AddMinutes(-1), now.AddHours(1), 600, 1, TheoryScoringMode.Highest, false,
                [new(TheoryQuestionKind.SingleChoice, null, 1, 10, [])]) }, ct);
            await Assert.That(activityResponse.StatusCode).IsEqualTo(HttpStatusCode.OK);
            var activity = (await activityResponse.Content.ReadFromJsonAsync<TheoryActivitySummary>(ct))!;
            var activityPath = $"/api/v1/theory/activities/{activity.Id}";
            using (var publish = await client.PostAsJsonAsync(activityPath + "/operations", new { action = TheoryActivityAction.Publish }, ct))
                await Assert.That(publish.StatusCode).IsEqualTo(HttpStatusCode.OK);
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", consumer.ToString());
            using var issued = await client.PostAsJsonAsync(activityPath + "/papers", new { requestId = Guid.NewGuid(), expectedIssuedCount = 0 }, ct);
            await Assert.That(issued.StatusCode).IsEqualTo(HttpStatusCode.OK);
            var paper = (await issued.Content.ReadFromJsonAsync<TheoryPaperView>(ct))!;
            var rawPaper = await issued.Content.ReadAsStringAsync(ct);
            await Assert.That(rawPaper).DoesNotContain("isCorrect");
            await Assert.That(rawPaper).DoesNotContain("PRIVATE ANSWER EXPLANATION");
            var paperPath = $"{activityPath}/papers/{paper.Paper!.Id}";
            using (var imageRead = await client.GetAsync($"{paperPath}/images/{image.FileId}?staff=false", ct))
                await Assert.That(imageRead.StatusCode).IsEqualTo(HttpStatusCode.OK);
            var item = paper.Paper.Questions.Single();
            using var answer = await client.PutAsJsonAsync($"{paperPath}/answers/{item.Id}", new { expectedRevision = item.AnswerRevision,
                requestId = Guid.NewGuid(), optionIds = new[] { item.Options.Single(option => option.Content == "alpha").Id } }, ct);
            await Assert.That(answer.StatusCode).IsEqualTo(HttpStatusCode.OK);
            var answered = (await answer.Content.ReadFromJsonAsync<TheoryPaperView>(ct))!;
            await Assert.That(answered.Paper!.Score).IsNull();
            using var submit = await client.PostAsJsonAsync(paperPath + "/submit", new { expectedRevision = answered.Paper.AnswerRevision }, ct);
            var graded = (await submit.Content.ReadFromJsonAsync<TheoryPaperView>(ct))!;
            await Assert.That(graded.Paper!.Score).IsEqualTo(10L);
            await Assert.That(graded.Paper.Questions).IsEmpty();
            using (var imageRead = await client.GetAsync($"{paperPath}/images/{image.FileId}?staff=false", ct))
                await Assert.That(imageRead.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
            using (var review = await client.GetAsync($"{activityPath}/reviews/{paper.Paper.Id}", ct))
                await Assert.That(review.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", owner.ToString());
            using (var read = await client.GetAsync($"{path}/{saved.QuestionId}", ct))
                await Assert.That((await read.Content.ReadAsStringAsync(ct)).Contains("PRIVATE ANSWER EXPLANATION")).IsTrue();
            await using (var db = new NoCtfDbContext(options))
            {
                var banned = await db.Users.SingleAsync(user => user.Id == owner, ct);
                banned.AccountStatus = UserAccountStatus.Banned;
                await db.SaveChangesAsync(ct);
            }
            using (var read = await client.GetAsync($"{path}/{saved.QuestionId}", ct))
                await Assert.That(read.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
        });
    }

    [Test]
    public async Task All_bank_and_question_failures_have_explicit_localized_messages()
    {
        foreach (var failure in Enum.GetValues<TheoryBankFailure>().Cast<Enum>().Concat(Enum.GetValues<TheoryQuestionFailure>().Cast<Enum>()))
        {
            var message = ApiMessages.For(failure);
            await Assert.That(message.Id == ApiMessageId.RequestFailed).IsFalse();
            await Assert.That(message.Text).IsNotEmpty();
        }
    }

    private static User Actor(Guid id, string name, UserRole role) => new()
    {
        Id = id, UserName = name, NormalizedUserName = name.ToUpperInvariant(), Email = $"{name}@example.invalid",
        NormalizedEmail = $"{name}@example.invalid".ToUpperInvariant(), Role = role, PasswordHash = "not-a-login"
    };

    private sealed class TestBearer(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var header = Request.Headers.Authorization.ToString();
            if (!header.StartsWith("Bearer ", StringComparison.Ordinal) || !Guid.TryParse(header[7..], out var id))
                return Task.FromResult(AuthenticateResult.NoResult());
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim(ClaimTypes.NameIdentifier, id.ToString())], Scheme.Name)), Scheme.Name)));
        }
    }

    private sealed class TheoryImageDirectory : IDisposable
    {
        public string Path { get; } = Directory.CreateTempSubdirectory("noctf-theory-http-").FullName;
        public void Dispose()
        {
            var root = System.IO.Path.GetFullPath(Path);
            var temporary = System.IO.Path.TrimEndingDirectorySeparator(System.IO.Path.GetFullPath(System.IO.Path.GetTempPath())) + System.IO.Path.DirectorySeparatorChar;
            if (!root.StartsWith(temporary, StringComparison.OrdinalIgnoreCase)
                || !System.IO.Path.GetFileName(root).StartsWith("noctf-theory-http-", StringComparison.Ordinal))
                throw new InvalidOperationException("Unexpected test image directory.");
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }
}
