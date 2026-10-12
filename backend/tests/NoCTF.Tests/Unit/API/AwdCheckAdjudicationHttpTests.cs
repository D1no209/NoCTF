using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using FastEndpoints;
using FastEndpoints.Swagger;
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
using NoCTF.Application.Competitions.Awd;
using NoCTF.Application.Teams.Moderation;
using NoCTF.Domain.Competitions.Awd;

namespace NoCTF.Tests.Unit.API;

public sealed class AwdCheckAdjudicationHttpTests
{
    [Test]
    public async Task Adjudication_requires_judge_permission_and_strict_actions_and_returns_safe_evidence()
    {
        var user = Substitute.For<IUserContext>();
        var actor = Guid.NewGuid();
        user.UserId.Returns(actor);
        var competitionId = Guid.NewGuid();
        var factId = Guid.NewGuid();
        var store = Substitute.For<IAwdCheckAdjudicationStore>();
        var authorizer = Substitute.For<ICompetitionModerationAuthorizer>();
        var allowed = false;
        authorizer.CanJudgeAsync(actor, competitionId, Arg.Any<CancellationToken>()).Returns(_ => allowed);
        var view = new AwdCheckEvidenceView(factId, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            new() { Health = AwdHealth.InternalError, Settlement = AwdSettlementState.PendingAdjudication,
                Reason = AwdCheckReason.LateReceipt, AttemptId = Guid.NewGuid(), ReceivedAt = DateTimeOffset.UtcNow,
                Flags = [new() { FlagId = Guid.NewGuid() }] });
        store.ReadAsync(competitionId, factId, Arg.Any<CancellationToken>()).Returns(view);
        store.ApplyAsync(Arg.Any<AwdCheckAdjudicationCommand>(), Arg.Any<CancellationToken>())
            .Returns(new AwdCheckAdjudicationResult(AwdCheckAdjudicationState.Conflict));
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddProblemDetails();
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddSingleton(user);
        builder.Services.AddSingleton(authorizer);
        builder.Services.AddSingleton(store);
        builder.Services.AddScoped<AdjudicateAwdCheck>();
        builder.Services.AddFastEndpoints(options =>
        {
            options.DisableAutoDiscovery = true;
            options.Assemblies = [typeof(AdjudicateAwdCheckEndpoint).Assembly];
            options.Filter = type => type == typeof(AdjudicateAwdCheckEndpoint)
                || type == typeof(AdjudicateAwdCheckValidator) || type == typeof(GetAwdCheckEvidenceEndpoint);
        });
        builder.Services.SwaggerDocument();
        builder.Services.AddAuthentication("Bearer").AddScheme<AuthenticationSchemeOptions, TestAuthentication>("Bearer", _ => { });
        builder.Services.AddAuthorization();
        await using var app = builder.Build();
        app.UseAuthentication();
        app.UseAuthorization();
        app.UseNoCtfEndpoints();
        await app.StartAsync();
        using var client = app.GetTestClient();
        var path = $"/api/v1/admin/competitions/{competitionId}/awd/checks/{factId}";
        var request = new { action = "AcceptEvidence", expectedAttemptId = view.Evidence.AttemptId, expectedReceivedAt = view.Evidence.ReceivedAt, reason = "Reviewed evidence" };
        using var forbiddenRead = await client.GetAsync(path);
        using var forbiddenWrite = await client.PostAsJsonAsync(path + "/adjudication", request);
        await Assert.That(forbiddenRead.StatusCode).IsEqualTo(HttpStatusCode.Forbidden);
        await Assert.That(forbiddenWrite.StatusCode).IsEqualTo(HttpStatusCode.Forbidden);
        await Assert.That(store.ReceivedCalls()).IsEmpty();
        allowed = true;
        using var response = await client.GetAsync(path);
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        await Assert.That(json.RootElement.GetProperty("health").GetString()).IsEqualTo("InternalError");
        await Assert.That(json.RootElement.GetProperty("settlement").GetString()).IsEqualTo("PendingAdjudication");
        await Assert.That(json.RootElement.GetProperty("flags")[0].TryGetProperty("value", out _)).IsFalse();
        await Assert.That(json.RootElement.GetProperty("flags")[0].TryGetProperty("privateReadContext", out _)).IsFalse();
        using var invalid = await client.PostAsJsonAsync(path + "/adjudication",
            new { action = 0, expectedAttemptId = view.Evidence.AttemptId, expectedReceivedAt = view.Evidence.ReceivedAt, reason = "Reviewed" });
        await Assert.That(invalid.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
        using var conflict = await client.PostAsJsonAsync(path + "/adjudication", request);
        await Assert.That(conflict.StatusCode).IsEqualTo(HttpStatusCode.Conflict);
        await store.Received(1).ApplyAsync(Arg.Is<AwdCheckAdjudicationCommand>(command =>
            command != null && command.ActorUserId == actor && command.CompetitionId == competitionId && command.GameplayFactId == factId
            && command.ExpectedAttemptId == view.Evidence.AttemptId && command.ExpectedReceivedAt == view.Evidence.ReceivedAt), Arg.Any<CancellationToken>());
        using var problem = JsonDocument.Parse(await conflict.Content.ReadAsStringAsync());
        await Assert.That(problem.RootElement.GetProperty("code").GetString()).IsEqualTo("Conflict");
        await Assert.That(problem.RootElement.GetProperty("detail").GetString()).IsNotNullOrEmpty();
    }

    private sealed class TestAuthentication(IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger, UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync() => Task.FromResult(AuthenticateResult.Success(
            new AuthenticationTicket(new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", "test")], Scheme.Name)), Scheme.Name)));
    }
}
