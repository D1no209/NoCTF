using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using NoCTF.API.Security;
using NoCTF.Application.Authentication.Account;
using NoCTF.Application.Authentication.EmailVerification;
using NoCTF.Domain.Identity;
using NSubstitute;

namespace NoCTF.Tests.Unit.API;

public sealed class EmailVerificationGateTests
{
    [Test]
    [Arguments("GET", true, true)]
    [Arguments("HEAD", true, true)]
    [Arguments("POST", true, false)]
    [Arguments("GET", false, false)]
    public async Task Only_explicitly_anonymous_reads_bypass_email_qualification(
        string method, bool anonymous, bool allowed)
    {
        using var services = new ServiceCollection().AddLogging().AddProblemDetails().BuildServiceProvider();
        var context = new DefaultHttpContext { RequestServices = services,
            User = new ClaimsPrincipal(new ClaimsIdentity([
                new Claim("sub", Guid.NewGuid().ToString()), new Claim("user_kind", "Human")], "test")) };
        context.Request.Method = method;
        context.Request.Path = "/api/v1/competitions";
        context.Response.Body = new MemoryStream();
        context.SetEndpoint(new Endpoint(_ => Task.CompletedTask,
            anonymous ? new EndpointMetadataCollection(new AllowAnonymousAttribute()) : new EndpointMetadataCollection(), "test"));
        var configuration = Substitute.For<IEmailVerificationConfigurationStore>();
        configuration.GetAsync(Arg.Any<CancellationToken>()).Returns(new EmailVerificationConfigurationView(
            true, "https://ctf.example.test", 30, 60, 30, 60, 5, "smtp", 25,
            default(SmtpSecurityMode), "", false, "sender@example.test", "CTF", 30, DateTimeOffset.UtcNow));
        var called = false;
        await new EmailVerificationGateMiddleware(_ => { called = true; return Task.CompletedTask; })
            .InvokeAsync(context, configuration, Substitute.For<IUserAuthenticationStore>());
        await Assert.That(called).IsEqualTo(allowed);
        await Assert.That(context.Response.StatusCode).IsEqualTo(allowed ? 200 : 403);
        if (allowed) await configuration.DidNotReceive().GetAsync(Arg.Any<CancellationToken>());
    }
}
