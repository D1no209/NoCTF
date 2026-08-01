using FastEndpoints;
using FluentValidation;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using System.Text.Json.Serialization;
using NoCTF.Application.Administration;
using NoCTF.Domain.Identity;

namespace NoCTF.API.Endpoints.Administration.Platform;

public sealed class CreatePlatformBotRequest
{
    private string userName = string.Empty;

    public string UserName
    {
        get => userName;
        set => userName = value?.Trim() ?? string.Empty;
    }
    [JsonConverter(typeof(JsonStringEnumConverter<UserRole>))]
    public UserRole Role { get; set; } = UserRole.Organizer;
}

public sealed class CreatePlatformBotValidator : Validator<CreatePlatformBotRequest>
{
    public CreatePlatformBotValidator()
    {
        RuleFor(request => request.UserName)
            .NotEmpty()
            .Length(3, 64)
            .Matches("^[A-Za-z0-9_-]+$");
        RuleFor(request => request.Role).Equal(UserRole.Organizer);
    }
}

public sealed class CreatePlatformBotEndpoint(ManagePlatform platform)
    : Endpoint<CreatePlatformBotRequest,
        Results<Created<PlatformUserResponse>, ProblemHttpResult>>
{
    public override void Configure()
    {
        Post("/admin/platform/bots");
        AuthSchemes("Bearer");
        Roles("Administrator");
        Description(builder => builder.WithName("AdminPlatformCreateBot"));
        Summary(summary =>
        {
            summary.Summary = "Creates a non-interactive Organizer Bot.";
            summary.Description =
                "Creates a Bot identity that cannot use password login or refresh tokens.";
        });
    }

    public override async Task<
        Results<Created<PlatformUserResponse>, ProblemHttpResult>> ExecuteAsync(
        CreatePlatformBotRequest request,
        CancellationToken ct)
    {
        var result = await platform.CreateBotAsync(
            request.UserName,
            request.Role,
            DateTimeOffset.UtcNow,
            ct);
        if (result.State == CreateBotState.UserNameConflict)
            return TypedResults.Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "Bot was not created.",
                detail: "The requested user name is already in use.");
        if (result.State is CreateBotState.InvalidUserName or CreateBotState.InvalidRole)
            return TypedResults.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "Bot was not created.",
                detail: result.State == CreateBotState.InvalidRole
                    ? "GitOps Bots must use the Organizer role."
                    : "Bot UserName must contain 3..64 ASCII letters, digits, '_' or '-'.");

        var response = PlatformUserMapping.ToResponse(result.User!);
        return TypedResults.Created($"/api/v1/admin/platform/users/{response.Id}", response);
    }
}
