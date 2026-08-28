using System.Net;
using System.Net.Http.Json;
using System.Threading.RateLimiting;
using FastEndpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using NoCTF.API.Composition;
using NoCTF.API.Endpoints.Authentication;
using NoCTF.Application.Authentication.Account;

namespace NoCTF.Tests.Unit.API;

public sealed class EmailVerificationRequestEndpointTests
{
    [Test]
    public async Task Request_requires_a_valid_email_address()
    {
        var valid = new RequestEmailVerificationValidator().Validate(
            new RequestEmailVerificationRequest { Email = "player@example.test" });
        var invalid = new RequestEmailVerificationValidator().Validate(
            new RequestEmailVerificationRequest { Email = "not-an-email" });

        await Assert.That(valid.IsValid).IsTrue();
        await Assert.That(invalid.IsValid).IsFalse();
    }

    [Test]
    [Arguments(EmailVerificationState.Issued)]
    [Arguments(EmailVerificationState.UserNotFound)]
    [Arguments(EmailVerificationState.AlreadyVerified)]
    [Arguments(EmailVerificationState.RateLimited)]
    [Arguments(EmailVerificationState.DeliveryNotConfigured)]
    [Arguments(EmailVerificationState.Disabled)]
    public async Task Request_is_always_accepted_without_account_disclosure(
        EmailVerificationState state)
    {
        var store = new EmailVerificationStore(state);
        await using var app = await CreateApplicationAsync(store);
        using var response = await app.GetTestClient().PostAsJsonAsync(
            "/api/v1/auth/email-verification/request",
            new { email = " Player@Example.Test " });

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Accepted);
        await Assert.That(store.RequestedEmail).IsEqualTo(" Player@Example.Test ");
        await Assert.That(store.RequestCount).IsEqualTo(1);
    }

    private static async Task<WebApplication> CreateApplicationAsync(
        IEmailVerificationStore store)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddProblemDetails();
        builder.Services.AddFastEndpoints(options =>
        {
            options.DisableAutoDiscovery = true;
            options.Assemblies = [typeof(RequestEmailVerificationEndpoint).Assembly];
            options.Filter = type =>
                type == typeof(RequestEmailVerificationEndpoint)
                || type == typeof(RequestEmailVerificationValidator);
        });
        builder.Services.AddRateLimiter(options => options.AddFixedWindowLimiter(
            "email-verification-request",
            limiter =>
            {
                limiter.PermitLimit = 20;
                limiter.Window = TimeSpan.FromMinutes(1);
                limiter.QueueLimit = 0;
                limiter.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
            }));
        builder.Services.AddSingleton(store);
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddScoped<RequestEmailVerification>();

        var app = builder.Build();
        app.UseRateLimiter();
        app.UseNoCtfEndpoints();
        await app.StartAsync();
        return app;
    }

    private sealed class EmailVerificationStore(EmailVerificationState state)
        : IEmailVerificationStore
    {
        public string? RequestedEmail { get; private set; }
        public int RequestCount { get; private set; }

        public Task<bool> IsRequiredAsync(CancellationToken cancellationToken) =>
            Task.FromResult(true);

        public Task<EmailVerificationState> IssueByEmailAsync(
            string email,
            DateTimeOffset now,
            CancellationToken cancellationToken)
        {
            RequestedEmail = email;
            RequestCount++;
            return Task.FromResult(state);
        }

        public Task<EmailVerificationState> IssueAsync(
            Guid userId,
            DateTimeOffset now,
            CancellationToken cancellationToken) => Task.FromResult(state);

        public Task<EmailVerificationState> VerifyAsync(
            string token,
            DateTimeOffset now,
            CancellationToken cancellationToken) =>
            Task.FromResult(EmailVerificationState.InvalidOrExpired);
    }
}
