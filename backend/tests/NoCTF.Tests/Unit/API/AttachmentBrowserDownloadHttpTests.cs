using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
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
using NoCTF.API.Security;
using NoCTF.Application.Authentication.Mfa;
using NoCTF.Domain.Identity.Mfa;

namespace NoCTF.Tests.Unit.API;

public sealed class AttachmentBrowserDownloadHttpTests
{
    [Test]
    public async Task Native_get_preserves_JWT_and_current_MFA_validation_and_never_grants_other_routes()
    {
        const string key = "browser-download-test-key-at-least-thirty-two-characters";
        var userId = Guid.NewGuid(); MfaFailure? denial = null;
        var mfa = Substitute.For<IMfaAuthenticationStore>();
        mfa.ValidateContextAsync(userId, 1, Arg.Any<AuthenticationContext?>(), Arg.Any<CancellationToken>()).Returns(_ => denial);
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = Environments.Development }); builder.WebHost.UseTestServer();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["Authentication:SigningKey"] = key, ["RunnerScoring:SigningKey"] = key });
        builder.Services.AddDataProtection(); builder.Services.AddSingleton(TimeProvider.System); builder.Services.AddSingleton(mfa);
        builder.Services.AddNoCtfAuthentication(builder.Configuration);
        await using var app = builder.Build(); app.UseAuthentication(); app.UseAuthorization();
        app.MapPost("/prepare", (HttpContext context, AttachmentBrowserDownload handoff) => { handoff.Write(context, "/attachment"); return TypedResults.NoContent(); }).RequireAuthorization();
        app.MapGet("/attachment", () => TypedResults.Stream(new MemoryStream(Encoding.UTF8.GetBytes("file payload")), "application/octet-stream", "附件.zip"))
            .WithMetadata(new AttachmentBrowserDownloadMetadata()).RequireAuthorization();
        app.MapGet("/other", () => TypedResults.Ok("other business")).RequireAuthorization();
        await app.StartAsync(); using var client = app.GetTestClient();
        using var deniedPreparation = await client.PostAsync("/prepare", null); await Assert.That(deniedPreparation.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
        var token = new JwtSecurityToken("NoCTF", "NoCTF.Api", [new("sub", userId.ToString()), new("token_type", "access"), new("token_version", "1")],
            DateTime.UtcNow.AddSeconds(-1), DateTime.UtcNow.AddMinutes(5), new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key)), SecurityAlgorithms.HmacSha256));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", new JwtSecurityTokenHandler().WriteToken(token));
        using var prepared = await client.PostAsync("/prepare", null); await Assert.That(prepared.StatusCode).IsEqualTo(HttpStatusCode.NoContent);
        var cookie = prepared.Headers.GetValues("Set-Cookie").Single().Split(';')[0]; client.DefaultRequestHeaders.Authorization = null; client.DefaultRequestHeaders.Add("Cookie", cookie);
        using var file = await client.GetAsync("/attachment"); await Assert.That(file.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(await file.Content.ReadAsStringAsync()).IsEqualTo("file payload");
        await Assert.That(file.Content.Headers.ContentDisposition!.DispositionType).IsEqualTo("attachment");
        using var other = await client.GetAsync("/other"); await Assert.That(other.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
        denial = MfaFailure.AccountUnavailable;
        using var revoked = await client.GetAsync("/attachment"); await Assert.That(revoked.StatusCode).IsEqualTo(HttpStatusCode.Forbidden);
    }
}
