using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.Tokens;
using NSubstitute;
using NoCTF.API.Composition;
using NoCTF.Application.Authentication.Mfa;
using NoCTF.Application.Common;
using NoCTF.Domain.Identity.Mfa;
using NoCTF.Infrastructure.Authentication.Mfa;

namespace NoCTF.Tests.Unit.API;

public sealed class MfaAuthenticationHttpTests
{
    [Test]
    public async Task Old_human_JWT_cannot_project_an_administrator_on_anonymous_routes_but_old_Bot_JWT_survives()
    {
        var humanId = Guid.NewGuid(); var botId = Guid.NewGuid();
        var store = Substitute.For<IMfaAuthenticationStore>();
        store.ValidateContextAsync(Arg.Any<Guid>(), 0, Arg.Any<AuthenticationContext?>(), Arg.Any<CancellationToken>())
            .Returns(call => call.Arg<Guid>() == botId ? (MfaFailure?)null : MfaFailure.PrimaryAuthenticationRequired);
        await using var app = await Application(store);
        using var client = app.GetTestClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Token(humanId, "Human"));
        using var protectedResponse = await client.GetAsync("/protected");
        await Assert.That(protectedResponse.StatusCode).IsEqualTo(HttpStatusCode.Forbidden);
        var projection = await client.GetFromJsonAsync<IdentityProjection>("/anonymous");
        await Assert.That(projection!.Administrator).IsFalse();
        await Assert.That(projection.Subject).IsNull();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Token(botId, "Bot"));
        using var botResponse = await client.GetAsync("/protected");
        await Assert.That(botResponse.StatusCode).IsEqualTo(HttpStatusCode.OK);
    }

    [Test]
    public async Task Browser_bound_Mfa_flow_has_no_business_identity_and_cannot_access_a_business_route()
    {
        var id = Guid.NewGuid(); var store = Substitute.For<IMfaAuthenticationStore>();
        store.ReadFlowAsync(Arg.Any<MfaBrowserCredential>(), Arg.Any<CancellationToken>()).Returns(
            OperationResult<MfaFlowView, MfaFailure>.Success(new(id, MfaChallengePurpose.Enrollment,
                DateTimeOffset.UtcNow.AddMinutes(10), 5, "test", false, "/")));
        await using var app = await Application(store);
        using var client = app.GetTestClient();
        client.DefaultRequestHeaders.Add("Cookie", $"NoCTF.Mfa.Dev={id:N}.{new string('x', 43)}");
        var flow = await client.GetFromJsonAsync<IdentityProjection>("/flow");
        await Assert.That(flow!.Administrator).IsFalse(); await Assert.That(flow.Subject).IsNull();
        using var business = await client.GetAsync("/protected");
        await Assert.That(business.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
        using var missingBrowser = app.GetTestClient();
        using var rejected = await missingBrowser.GetAsync("/flow");
        await Assert.That(rejected.StatusCode).IsEqualTo(HttpStatusCode.Gone);
    }

    private const string SigningKey = "test-signing-key-at-least-thirty-two-characters";
    private static string Token(Guid id, string kind)
    {
        var token = new JwtSecurityToken("NoCTF", "NoCTF.Api", [new("sub", id.ToString()),
            new(ClaimTypes.Role, "Administrator"), new("user_kind", kind), new("token_type", "access"), new("token_version", "0")],
            DateTime.UtcNow.AddSeconds(-1), DateTime.UtcNow.AddMinutes(5),
            new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(SigningKey)), SecurityAlgorithms.HmacSha256));
        return new JwtSecurityTokenHandler().WriteToken(token);
    }
    private static async Task<WebApplication> Application(IMfaAuthenticationStore store)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = Environments.Development });
        builder.WebHost.UseTestServer();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?> {
            ["Authentication:SigningKey"] = SigningKey, ["RunnerScoring:SigningKey"] = SigningKey });
        builder.Services.AddSingleton(store);
        builder.Services.AddNoCtfAuthentication(builder.Configuration);
        var app = builder.Build();
        app.UseAuthentication(); app.UseAuthorization();
        app.MapGet("/protected", () => TypedResults.Ok("business")).RequireAuthorization();
        app.MapGet("/anonymous", (HttpContext context) => TypedResults.Ok(Project(context.User))).AllowAnonymous();
        app.MapGet("/flow", (HttpContext context) => TypedResults.Ok(Project(context.User))).RequireAuthorization(AuthenticationRegistration.MfaFlowScheme);
        await app.StartAsync(); return app;
    }
    private static IdentityProjection Project(ClaimsPrincipal principal) => new(principal.IsInRole("Administrator"), principal.FindFirstValue(ClaimTypes.NameIdentifier));
    private sealed record IdentityProjection(bool Administrator, string? Subject);
}
