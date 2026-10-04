using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using FluentValidation.Results;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NoCTF.API.Composition;
using NoCTF.API.Endpoints.Authentication;
using NoCTF.API.Localization;
using NoCTF.API.Security;
using NoCTF.Application.Admission;
using NoCTF.Application.Authentication.Account;

namespace NoCTF.Tests.Integration.API;

public sealed class ApiLocalizationTests
{
    private static async Task<WebApplication> Server()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddLogging();
        builder.Services.AddProblemDetails();
        builder.Services.AddNoCtfLocalization();
        var app = builder.Build();
        app.UseRequestLocalization();
            app.Run(async context =>
            {
                await Task.Yield(); // Deliberately interleave request cultures.
                if (context.Request.Path == "/validation")
                {
                    var failures = new RegisterValidator().Validate(new RegisterRequest()).Errors;
                    await TypedResults.Problem(ApiValidationProblemFactory.Create(failures, 400)).ExecuteAsync(context);
                    return;
                }
                if (context.Request.Path == "/safety")
                {
                    var handler = new RequestSafetyExceptionHandler(NullLogger<RequestSafetyExceptionHandler>.Instance);
                    await handler.TryHandleAsync(context, new AdmissionRejectedException(AdmissionFailure.RateLimited), context.RequestAborted);
                    return;
                }
                var message = ApiMessages.For(RegisterUserFailureCode.EmailConflict);
                await ApiProblems.Problem(409, message, message, extensions: new Dictionary<string, object?>
                {
                    ["code"] = "EmailConflict",
                    ["culture"] = CultureInfo.CurrentUICulture.Name
                }).ExecuteAsync(context);
            });
        await app.StartAsync();
        return app;
    }

    [Test]
    [Arguments("en-US", "en")]
    [Arguments("zh-TW", "zh-CN")]
    [Arguments("fr;q=1, zh-CN;q=0.8, en;q=0.2", "zh-CN")]
    [Arguments("zh-CN;q=0, en;q=0.5", "en")]
    [Arguments("fr-FR", "en")]
    [Arguments("", "en")]
    public async Task Header_negotiation_uses_supported_families_and_weights(string header, string expected)
    {
        await using var server = await Server();
        using var client = server.GetTestClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/");
        request.Headers.TryAddWithoutValidation("Accept-Language", header);
        using var response = await client.SendAsync(request);
        var body = (await response.Content.ReadFromJsonAsync<JsonElement>());
        await Assert.That(body.GetProperty("culture").GetString()).IsEqualTo(expected);
        await Assert.That(response.Content.Headers.ContentLanguage.Single()).IsEqualTo(expected);
        await Assert.That(body.GetProperty("code").GetString()).IsEqualTo("EmailConflict");
        await Assert.That(body.GetProperty("messageKey").GetString()).IsEqualTo(ApiMessages.For(RegisterUserFailureCode.EmailConflict).Key);
    }

    [Test]
    public async Task Parallel_requests_do_not_leak_cultures_or_translate_machine_codes()
    {
        await using var server = await Server();
        using var client = server.GetTestClient();
        await Task.WhenAll(Enumerable.Range(0, 30).Select(async index =>
        {
            var locale = index % 2 == 0 ? "en" : "zh-CN";
            using var request = new HttpRequestMessage(HttpMethod.Get, "/");
            request.Headers.AcceptLanguage.ParseAdd(locale);
            using var response = await client.SendAsync(request);
            var body = await response.Content.ReadFromJsonAsync<JsonElement>();
            await Assert.That(body.GetProperty("culture").GetString()).IsEqualTo(locale);
            await Assert.That(body.GetProperty("code").GetString()).IsEqualTo("EmailConflict");
        }));
    }

    [Test]
    public async Task Validation_includes_localized_fields_and_semantic_descriptors()
    {
        await using var server = await Server();
        using var client = server.GetTestClient();
        client.DefaultRequestHeaders.AcceptLanguage.ParseAdd("zh-CN");
        using var response = await client.GetAsync("/validation");
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var errors = body.GetProperty("errors").GetProperty("UserName");
        await Assert.That(errors[0].GetString()).Contains("不能为空");
        var descriptor = body.GetProperty("errorMessages").GetProperty("UserName")[0];
        await Assert.That(descriptor.GetProperty("key").GetString()).IsEqualTo("api.validation.required");
        await Assert.That(descriptor.GetProperty("arguments").GetProperty("field").GetString()).IsEqualTo("User Name");
    }

    [Test]
    public async Task Request_safety_handler_localizes_feedback_and_keeps_retry_metadata()
    {
        await using var server = await Server();
        using var client = server.GetTestClient();
        client.DefaultRequestHeaders.AcceptLanguage.ParseAdd("zh-CN");
        using var response = await client.GetAsync("/safety");
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        await Assert.That((int)response.StatusCode).IsEqualTo(429);
        await Assert.That(body.GetProperty("code").GetString()).IsEqualTo("RateLimited");
        await Assert.That(body.GetProperty("messageKey").GetString()).IsEqualTo(ApiMessages.For(AdmissionFailure.RateLimited).Key);
        await Assert.That(response.Headers.Contains("Retry-After")).IsTrue();
    }

    [Test]
    public async Task Typed_failure_dto_keeps_the_code_but_ignores_supplied_diagnostics()
    {
        var failure = new ChangePasswordFailureResponse(
            ChangePasswordFailureCode.CurrentPasswordInvalid, "diagnostic containing protected data");
        await Assert.That(failure.Detail).IsEqualTo(ApiMessages.For(failure.Code).Text);
        await Assert.That(failure.MessageKey).IsEqualTo(ApiMessages.For(failure.Code).Key);
        await Assert.That(failure.MessageArguments.Count).IsEqualTo(0);
    }

    [Test]
    public async Task Custom_validation_keys_do_not_depend_on_english_text()
    {
        var failure = new ValidationFailure("Request", "raw diagnostic")
        {
            ErrorCode = ApiMessages.Key(ApiMessageId.RequestFailed)
        };
        var described = ApiValidationProblemFactory.Describe(failure);
        await Assert.That(described.Key).IsEqualTo("api.common.requestFailed");
        await Assert.That(described.Text).IsEqualTo(ApiMessages.Text(ApiMessageId.RequestFailed));
    }

    [Test]
    public async Task Validation_does_not_echo_password_values_or_unused_placeholder_arguments()
    {
        const string password = "private-password-value";
        var failures = new ChangePasswordValidator().Validate(new ChangePasswordRequest
        {
            CurrentPassword = password,
            NewPassword = password
        }).Errors;
        var problem = ApiValidationProblemFactory.Create(failures, 400);
        var json = JsonSerializer.Serialize(problem);
        await Assert.That(json.Contains(password, StringComparison.Ordinal)).IsFalse();
        await Assert.That(json.Contains("PropertyValue", StringComparison.Ordinal)).IsFalse();
    }

    [Test]
    public async Task Similar_failure_names_keep_their_own_business_meaning()
    {
        var previous = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = new CultureInfo("en");
            await Assert.That(ApiMessages.For(NoCTF.Application.Notifications.CompetitionAnnouncementFailure.NotFound).Text)
                .Contains("announcement");
            await Assert.That(ApiMessages.For(NoCTF.Application.Challenges.Bank.ChallengeTemplateDeleteFailure.NotFound).Text)
                .Contains("challenge template");
            await Assert.That(ApiMessages.For(NoCTF.Application.Challenges.Hints.HintUnlockFailure.NotFound).Text)
                .Contains("hint");
        }
        finally { CultureInfo.CurrentUICulture = previous; }
    }
}
