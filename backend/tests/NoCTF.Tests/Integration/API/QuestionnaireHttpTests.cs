using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
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
using NoCTF.API.Endpoints.Questionnaires.Sessions;
using NoCTF.API.Security;
using NoCTF.Application.Authentication.Account;
using NoCTF.Application.Authentication.Mfa;
using NoCTF.Application.Challenges.Bank;
using NoCTF.Application.Messaging;
using NoCTF.Application.Questionnaires.Sessions;
using NoCTF.Domain.Identity;
using NoCTF.Domain.Identity.Mfa;
using NoCTF.Domain.Questionnaires.Activities;
using NoCTF.Infrastructure.Authentication;
using NoCTF.Infrastructure.Authentication.Mfa;
using NoCTF.Infrastructure.Challenges.Bank;
using NoCTF.Infrastructure.Persistence;
using NoCTF.Infrastructure.Questionnaires.Sessions;

namespace NoCTF.Tests.Integration.API;

[Category("Integration")]
public sealed class QuestionnaireHttpTests
{
    [Test, Timeout(300_000)]
    public async Task Real_jwt_and_routes_enforce_bank_permissions_and_keep_answers_out_of_assigned_papers(CancellationToken ct)
    {
        await using var fixture = await QuestionnaireApplicationFixture.CreateAsync(ct);
        var activityId = await fixture.PublishAsync(QuestionnaireActivityKind.Practice, 600, 1, ct);
        var key = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        { ["Authentication:SigningKey"] = key, ["RunnerScoring:SigningKey"] = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)) }).Build();
        var builder = WebApplication.CreateBuilder(); builder.WebHost.UseKestrel().UseUrls("http://127.0.0.1:0");
        builder.Services.AddFastEndpoints(options =>
        {
            options.DisableAutoDiscovery = true; options.Assemblies = [typeof(GetChallengeTemplateEndpoint).Assembly];
            options.Filter = type => type == typeof(GetChallengeTemplateEndpoint) || type == typeof(IssueQuestionnaireSessionEndpoint) || type == typeof(IssueQuestionnaireSessionValidator)
                || type == typeof(GetQuestionnaireSessionEndpoint) || type == typeof(GetQuestionnaireSessionValidator);
        });
        builder.Services.SwaggerDocument();
        builder.Services.AddNoCtfAuthentication(configuration); builder.Services.AddAuthorization();
        builder.Services.AddHttpContextAccessor(); builder.Services.AddScoped<IUserContext, HttpUserContext>();
        builder.Services.AddDbContext<NoCtfDbContext>(options => options.UseNpgsql(fixture.Db.Database.GetConnectionString()));
        builder.Services.AddSingleton(fixture.Application.MessageBus);
        builder.Services.AddScoped<IPostCommitMessagePublisher, NoCTF.Infrastructure.Messaging.WolverinePostCommitMessagePublisher>();
        builder.Services.AddScoped<IChallengeBankStore, ChallengeBankStore>(); builder.Services.AddScoped<GetChallengeTemplate>();
        builder.Services.AddScoped<IQuestionnaireSessionStore, QuestionnaireSessionStore>();
        builder.Services.AddSingleton<IMfaCryptography, MfaCryptography>();
        builder.Services.AddSingleton<IOptions<EmailVerificationProtectionOptions>>(Options.Create(new EmailVerificationProtectionOptions
            { EncryptionKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)) }));
        builder.Services.AddSingleton<PlatformSecretProtector>(); builder.Services.AddScoped<IMfaAuthenticationStore, MfaAuthenticationStore>();
        await using var server = builder.Build(); server.UseAuthentication(); server.UseAuthorization(); server.UseNoCtfEndpoints();
        await server.StartAsync(ct);
        var address = server.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
        using var client = new HttpClient { BaseAddress = new Uri(address) };
        var issuer = new JwtIssuer(Options.Create(new AuthenticationTokenOptions { SigningKey = key }), TimeProvider.System);
        string Token(Guid id, UserRole role, string name) => issuer.Issue(new AuthenticatedUser(id, name, role, UserKind.Human, 0),
            new AuthenticationContext(AuthenticationMethod.Password, fixture.Now), fixture.Now).Token;
        void Authorize(Guid id, UserRole role, string name) => client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Token(id, role, name));
        var path = $"/api/v1/admin/challenges/{fixture.ChallengeId}";
        await Assert.That((await client.GetAsync(path, ct)).StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
        Authorize(fixture.FirstUserId, UserRole.User, "participant");
        await Assert.That((await client.GetAsync(path, ct)).StatusCode).IsEqualTo(HttpStatusCode.Forbidden);
        Authorize(fixture.OwnerId, UserRole.Organizer, "author");
        using (var response = await client.GetAsync(path, ct))
        {
            await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
            using var content = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
            await Assert.That(content.RootElement.TryGetProperty("mode", out _)).IsFalse();
            await Assert.That(content.RootElement.GetProperty("definition").TryGetProperty("mode", out _)).IsFalse();
            await Assert.That(content.RootElement.GetProperty("capabilities")[0].GetString()).IsEqualTo("Questionnaire");
        }
        (await fixture.Db.Users.SingleAsync(value => value.Id == fixture.SecondUserId, ct)).Role = UserRole.Organizer;
        await fixture.Db.SaveChangesAsync(ct); Authorize(fixture.SecondUserId, UserRole.Organizer, "other-author");
        await Assert.That((await client.GetAsync(path, ct)).StatusCode).IsEqualTo(HttpStatusCode.NotFound);
        Authorize(fixture.FirstUserId, UserRole.User, "participant");
        var requestId = Guid.NewGuid();
        using var issued = await client.PostAsJsonAsync($"/api/v1/questionnaires/activities/{activityId}/sessions", new { requestId, expectedIssuedCount = 0 }, ct);
        await Assert.That(issued.StatusCode).IsEqualTo(HttpStatusCode.OK);
        var text = await issued.Content.ReadAsStringAsync(ct);
        foreach (var field in new[] { "\"isCorrect\"", "\"correctOptions\"", "\"explanation\"", "\"flag\"" })
            await Assert.That(text.Contains(field, StringComparison.Ordinal)).IsFalse();
        using var paper = JsonDocument.Parse(text); var paperId = paper.RootElement.GetProperty("session").GetProperty("id").GetGuid();
        await Assert.That(paper.RootElement.GetProperty("session").GetProperty("questions").GetArrayLength()).IsEqualTo(2);
        using var replay = await client.PostAsJsonAsync($"/api/v1/questionnaires/activities/{activityId}/sessions", new { requestId, expectedIssuedCount = 0 }, ct);
        await Assert.That(replay.StatusCode).IsEqualTo(HttpStatusCode.OK);
        using var replayBody = JsonDocument.Parse(await replay.Content.ReadAsStringAsync(ct));
        await Assert.That(replayBody.RootElement.GetProperty("session").GetProperty("id").GetGuid()).IsEqualTo(paperId);
        Authorize(fixture.SecondUserId, UserRole.Organizer, "other-author");
        await Assert.That((await client.GetAsync($"/api/v1/questionnaires/activities/{activityId}/sessions/{paperId}", ct)).StatusCode).IsEqualTo(HttpStatusCode.NotFound);
        Authorize(fixture.OwnerId, UserRole.Organizer, "author");
        (await fixture.Db.Users.SingleAsync(value => value.Id == fixture.OwnerId, ct)).TokenVersion++;
        await fixture.Db.SaveChangesAsync(ct);
        using var revoked = await client.GetAsync(path, ct);
        await Assert.That(revoked.StatusCode).IsEqualTo(HttpStatusCode.Forbidden);
        using var revokedBody = JsonDocument.Parse(await revoked.Content.ReadAsStringAsync(ct));
        await Assert.That(revokedBody.RootElement.GetProperty("code").GetString()).IsEqualTo("AccountUnavailable");
    }
}
