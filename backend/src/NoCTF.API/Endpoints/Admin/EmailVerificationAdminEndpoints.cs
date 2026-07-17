using System.Security.Claims;
using FastEndpoints;
using Microsoft.EntityFrameworkCore;
using NoCTF.API.Auth;
using NoCTF.Infrastructure;

namespace NoCTF.API.Endpoints.Admin;

public sealed class GetEmailVerificationSettingsEndpoint(IEmailVerificationSettingsStore settingsStore)
    : EndpointWithoutRequest<EmailVerificationSettingsView>
{
    public override void Configure()
    {
        Get("/api/admin/email-verification");
        Roles("Admin");
    }

    public override async Task HandleAsync(CancellationToken ct)
        => await SendAsync(await settingsStore.GetViewAsync(ct), cancellation: ct);
}

public sealed class UpdateEmailVerificationSettingsEndpoint(IEmailVerificationSettingsStore settingsStore)
    : Endpoint<EmailVerificationSettingsUpdate, EmailVerificationSettingsView>, IAuditableEndpoint
{
    public override void Configure()
    {
        Put("/api/admin/email-verification");
        Roles("Admin");
    }

    public override async Task HandleAsync(EmailVerificationSettingsUpdate req, CancellationToken ct)
    {
        try
        {
            await SendAsync(await settingsStore.UpdateAsync(req, ct), cancellation: ct);
        }
        catch (InvalidOperationException)
        {
            await SendStringAsync("email_verification_configuration_invalid", 400, cancellation: ct);
        }
    }
}

public sealed class TestEmailVerificationSettingsEndpoint(
    IEmailVerificationSettingsStore settingsStore,
    IVerificationEmailSender emailSender,
    ApplicationDbContext db,
    IHostEnvironment environment,
    ILogger<TestEmailVerificationSettingsEndpoint> logger)
    : EndpointWithoutRequest<EmailVerificationTestResult>, IAuditableEndpoint
{
    public override void Configure()
    {
        Post("/api/admin/email-verification/test");
        Roles("Admin");
        Options(builder => builder.RequireRateLimiting("admin-email-test"));
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        if (!Guid.TryParse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var userId))
        {
            await SendUnauthorizedAsync(ct);
            return;
        }

        var recipient = await db.Users.AsNoTracking()
            .Where(user => user.Id == userId)
            .Select(user => user.Email)
            .FirstOrDefaultAsync(ct);
        if (string.IsNullOrWhiteSpace(recipient))
        {
            await SendNotFoundAsync(ct);
            return;
        }

        try
        {
            var options = await settingsStore.GetAsync(ct);
            options.Enabled = true;
            EmailVerificationOptionsValidator.Validate(options, environment.IsDevelopment());
            await emailSender.SendTestAsync(recipient, ct);
            await SendAsync(new EmailVerificationTestResult(true, "smtp_test_sent"), cancellation: ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogWarning(
                "Email verification SMTP test failed for administrator {UserId}: {ErrorType}",
                userId,
                exception.GetType().Name);
            await SendAsync(
                new EmailVerificationTestResult(false, "smtp_test_failed"),
                StatusCodes.Status502BadGateway,
                ct);
        }
    }
}
