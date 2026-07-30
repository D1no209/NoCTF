using FastEndpoints;
using FluentValidation;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.Application.Administration;
using NoCTF.Domain.Identity;

namespace NoCTF.API.Endpoints.Administration.Platform;

public sealed class CreatePlatformBotRequest
{
    public string UserName { get; set; } = string.Empty;
    public UserRole Role { get; set; }
}

public sealed class CreatePlatformBotValidator : Validator<CreatePlatformBotRequest>
{
    public CreatePlatformBotValidator()
    {
        RuleFor(request => request.UserName)
            .NotEmpty()
            .Length(3, 64)
            .Matches("^[A-Za-z0-9_-]+$");
        RuleFor(request => request.Role)
            .Must(role => role is UserRole.User or UserRole.Organizer)
            .WithMessage("Bot users can only have the User or Organizer role.");
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
            summary.Summary = "Creates a non-interactive bot user.";
            summary.Description = "Creates a bot with server-generated dummy email and password values.";
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

        var response = PlatformUserMapping.ToResponse(result.User!);
        return TypedResults.Created($"/api/v1/admin/platform/users/{response.Id}", response);
    }
}
