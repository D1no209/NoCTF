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
using NoCTF.Application.Authentication.PasswordReset;

namespace NoCTF.Tests.Unit.API;

public sealed class PasswordResetEndpointTests
{
    [Test]
    public async Task Request_requires_a_valid_email_address()
    {
        var valid = new RequestPasswordResetValidator().Validate(new RequestPasswordResetRequest
        {
            Email = "player@example.test"
        });
        var invalid = new RequestPasswordResetValidator().Validate(new RequestPasswordResetRequest
        {
            Email = "not-an-email"
        });

        await Assert.That(valid.IsValid).IsTrue();
        await Assert.That(invalid.IsValid).IsFalse();
    }

    [Test]
    public async Task Completion_accepts_an_eight_character_password()
    {
        var valid = new CompletePasswordResetValidator().Validate(new CompletePasswordResetRequest
        {
            Token = "single-use-token",
            NewPassword = "eight888"
        });
        var shortPassword = new CompletePasswordResetValidator().Validate(
            new CompletePasswordResetRequest
            {
                Token = "single-use-token",
                NewPassword = "seven77"
            });

        await Assert.That(valid.IsValid).IsTrue();
        await Assert.That(shortPassword.IsValid).IsFalse();
    }

    [Test]
    [Arguments(PasswordResetRequestState.Queued)]
    [Arguments(PasswordResetRequestState.Ignored)]
    [Arguments(PasswordResetRequestState.RateLimited)]
    [Arguments(PasswordResetRequestState.DeliveryNotConfigured)]
    public async Task Request_is_always_accepted_without_account_disclosure(
        PasswordResetRequestState state)
    {
        await using var app = await CreateApplicationAsync(new PasswordResetStore(state));
        using var response = await app.GetTestClient().PostAsJsonAsync(
            "/api/v1/auth/password-reset/request",
            new { email = "player@example.test" });

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Accepted);
    }

    [Test]
    public async Task Invalid_or_expired_completion_is_a_stable_typed_failure()
    {
        await using var app = await CreateApplicationAsync(new PasswordResetStore(
            PasswordResetRequestState.Ignored,
            PasswordResetCompletionState.InvalidOrExpired));
        using var response = await app.GetTestClient().PostAsJsonAsync(
            "/api/v1/auth/password-reset/complete",
            new { token = "expired-token", newPassword = "new-pass" });
        var failure = await response.Content
            .ReadFromJsonAsync<CompletePasswordResetFailureResponse>();

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
        await Assert.That(failure!.Code)
            .IsEqualTo(CompletePasswordResetFailureCode.InvalidOrExpired);
    }

    [Test]
    public async Task Successful_completion_clears_the_current_refresh_cookie()
    {
        await using var app = await CreateApplicationAsync(new PasswordResetStore(
            PasswordResetRequestState.Ignored,
            PasswordResetCompletionState.Reset));
        using var response = await app.GetTestClient().PostAsJsonAsync(
            "/api/v1/auth/password-reset/complete",
            new { token = "valid-token", newPassword = "new-pass" });

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NoContent);
        await Assert.That(response.Headers.TryGetValues("Set-Cookie", out var cookies)).IsTrue();
        var cookie = cookies?.Single() ?? string.Empty;
        await Assert.That(cookie).Contains("__Secure-noctf_refresh=");
        await Assert.That(cookie).Contains("path=/api/v1/auth");
    }

    private static async Task<WebApplication> CreateApplicationAsync(IPasswordResetStore store)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddProblemDetails();
        builder.Services.AddFastEndpoints(options =>
        {
            options.DisableAutoDiscovery = true;
            options.Assemblies = [typeof(RequestPasswordResetEndpoint).Assembly];
            options.Filter = type =>
                type == typeof(RequestPasswordResetEndpoint)
                || type == typeof(RequestPasswordResetValidator)
                || type == typeof(CompletePasswordResetEndpoint)
                || type == typeof(CompletePasswordResetValidator);
        });
        builder.Services.AddRateLimiter(options => options.AddFixedWindowLimiter(
            "password-reset-request",
            limiter =>
            {
                limiter.PermitLimit = 20;
                limiter.Window = TimeSpan.FromMinutes(1);
                limiter.QueueLimit = 0;
                limiter.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
            }));
        builder.Services.AddSingleton(store);
        builder.Services.AddScoped<RequestPasswordReset>();
        builder.Services.AddScoped<CompletePasswordReset>();

        var app = builder.Build();
        app.UseRateLimiter();
        app.UseNoCtfEndpoints();
        await app.StartAsync();
        return app;
    }

    private sealed class PasswordResetStore(
        PasswordResetRequestState requestState,
        PasswordResetCompletionState completionState =
            PasswordResetCompletionState.InvalidOrExpired) : IPasswordResetStore
    {
        public Task<PasswordResetRequestState> IssueAsync(
            string email,
            DateTimeOffset now,
            CancellationToken cancellationToken) => Task.FromResult(requestState);

        public Task<PasswordResetCompletionState> CompleteAsync(
            string token,
            string newPassword,
            DateTimeOffset now,
            CancellationToken cancellationToken) => Task.FromResult(completionState);
    }
}
